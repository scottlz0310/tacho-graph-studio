using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using TachoGraphStudio.Core.Auth;
using TachoGraphStudio.Core.Persistence;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Settings;

public sealed class DpapiSecretStore : ISecretStore, IDisposable
{
    // version 3 で email を業者コードから導出する形式へ変更。version 2 は、現在の
    // machinery-report-system と共通のメール規則へ変換できる場合だけ読み込む。
    private const int CurrentVersion = 3;
    private const int LegacyVersion = 2;

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

        if (document.Version is not (CurrentVersion or LegacyVersion))
        {
            throw new InvalidDataException(
                $"Supabase 資格情報のバージョン {document.Version} はサポートされていません。");
        }

        byte[] cipherText = Convert.FromBase64String(document.ProtectedPayload);
        byte[] plainText = ProtectedData.Unprotect(cipherText, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            if (document.Version == LegacyVersion)
            {
                return ReadLegacyCredentials(plainText);
            }

            SecretPayload payload = JsonSerializer.Deserialize<SecretPayload>(plainText, SerializerOptions)
                ?? throw new InvalidDataException("Supabase 資格情報の復号結果が JSON オブジェクトではありません。");

            return SupabaseCredentials.Create(
                new Uri(payload.ProjectUrl),
                payload.AnonKey,
                payload.VendorCode,
                payload.Password);
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
            VendorCode = credentials.VendorCode,
            Password = credentials.Password,
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

    private static SupabaseCredentials ReadLegacyCredentials(byte[] plainText)
    {
        LegacySecretPayload payload = JsonSerializer.Deserialize<LegacySecretPayload>(
                plainText,
                SerializerOptions)
            ?? throw new InvalidDataException("Supabase 資格情報の復号結果が JSON オブジェクトではありません。");

        if (!SupabaseVendorIdentity.TryGetVendorCode(payload.Email, out string vendorCode))
        {
            throw new InvalidDataException(
                "旧形式の Supabase 資格情報から業者コードを導出できません。接続設定を再入力してください。");
        }

        return SupabaseCredentials.Create(
            new Uri(payload.ProjectUrl),
            payload.AnonKey,
            vendorCode,
            payload.Password);
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

        [JsonRequired]
        public string VendorCode { get; init; } = string.Empty;

        [JsonRequired]
        public string Password { get; init; } = string.Empty;
    }

    private sealed class LegacySecretPayload
    {
        [JsonRequired]
        public string ProjectUrl { get; init; } = string.Empty;

        [JsonRequired]
        public string AnonKey { get; init; } = string.Empty;

        [JsonRequired]
        public string Email { get; init; } = string.Empty;

        [JsonRequired]
        public string Password { get; init; } = string.Empty;
    }
}
