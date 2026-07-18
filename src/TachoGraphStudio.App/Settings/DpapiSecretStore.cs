using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using TachoGraphStudio.Core.Persistence;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

public sealed class DpapiSecretStore : ISecretStore, IDisposable
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly AtomicJsonFile<SecretDocument> _file;

    public DpapiSecretStore(string secretsFilePath)
    {
        if (string.IsNullOrWhiteSpace(secretsFilePath))
        {
            throw new ArgumentException("Supabase 資格情報のファイルパスを指定してください。", nameof(secretsFilePath));
        }

        _file = new AtomicJsonFile<SecretDocument>(
            Path.GetFullPath(secretsFilePath),
            SerializerOptions,
            "Supabase 資格情報");
    }

    public async Task<SupabaseCredentials?> ReadAsync(CancellationToken cancellationToken = default)
    {
        SecretDocument? document = await _file.ReadAsync(cancellationToken);
        if (document is null)
        {
            return null;
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Supabase 資格情報のバージョン {document.Version} はサポートされていません。");
        }

        byte[] cipherText = Convert.FromBase64String(document.ProtectedPayload);
        byte[] plainText = ProtectedData.Unprotect(cipherText, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            SecretPayload payload = JsonSerializer.Deserialize<SecretPayload>(plainText, SerializerOptions)
                ?? throw new InvalidDataException("Supabase 資格情報の復号結果が JSON オブジェクトではありません。");

            return SupabaseCredentials.Create(new Uri(payload.ProjectUrl), payload.AnonKey);
        }
        finally
        {
            Array.Clear(plainText);
        }
    }

    public Task WriteAsync(SupabaseCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        SecretPayload payload = new()
        {
            ProjectUrl = credentials.ProjectUrl.AbsoluteUri,
            AnonKey = credentials.AnonKey,
        };

        byte[] plainText = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        byte[] cipherText;
        try
        {
            cipherText = ProtectedData.Protect(plainText, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            Array.Clear(plainText);
        }

        SecretDocument document = new()
        {
            Version = CurrentVersion,
            ProtectedPayload = Convert.ToBase64String(cipherText),
        };

        return _file.WriteAsync(document, cancellationToken);
    }

    public void Dispose()
    {
        _file.Dispose();
    }

    private sealed class SecretDocument
    {
        public int Version { get; init; }

        [JsonRequired]
        public string ProtectedPayload { get; init; } = string.Empty;
    }

    private sealed class SecretPayload
    {
        [JsonRequired]
        public string ProjectUrl { get; init; } = string.Empty;

        [JsonRequired]
        public string AnonKey { get; init; } = string.Empty;
    }
}
