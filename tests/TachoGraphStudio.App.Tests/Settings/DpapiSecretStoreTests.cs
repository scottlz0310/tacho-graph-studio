using System.Security.Cryptography;
using System.Text.Json;

using TachoGraphStudio.App.Settings;
using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.App.Tests.Settings;

public sealed class DpapiSecretStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.DpapiTests-{Guid.NewGuid():N}");

    private string SecretsPath => Path.Combine(_temporaryDirectory, "supabase.secret.json");

    [Fact]
    public async Task WriteAndReadAsync_RoundTripsCurrentCredentials()
    {
        SupabaseCredentials expected = SupabaseCredentials.Create(
            new Uri("https://example.supabase.co"),
            "test-anon-key",
            "test-vendor",
            "test-password");

        using (DpapiSecretStore store = new(SecretsPath))
        {
            await store.WriteAsync(expected);
        }

        using DpapiSecretStore reader = new(SecretsPath);
        SupabaseCredentials? actual = await reader.ReadAsync();
        Assert.NotNull(actual);

        Assert.Equal(expected.ProjectUrl, actual!.ProjectUrl);
        Assert.Equal(expected.AnonKey, actual.AnonKey);
        Assert.Equal(expected.VendorCode, actual.VendorCode);
        Assert.Equal(expected.Password, actual.Password);
        string persistedDocument = await File.ReadAllTextAsync(SecretsPath);
        Assert.DoesNotContain(expected.Password, persistedDocument, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_LegacyVersionWithSharedEmailDerivesVendorCode()
    {
        await WriteProtectedDocumentAsync(
            version: 2,
            new LegacySecretPayload(
                "https://example.supabase.co",
                "test-anon-key",
                "TEST-VENDOR@ZAMA-SYS.INTERNAL",
                "test-password"));

        using DpapiSecretStore store = new(SecretsPath);
        SupabaseCredentials? credentials = await store.ReadAsync();
        Assert.NotNull(credentials);

        Assert.Equal("test-vendor", credentials!.VendorCode);
        Assert.Equal("test-password", credentials.Password);
    }

    [Fact]
    public async Task ReadAsync_LegacyVersionWithUnknownEmailRequiresReentry()
    {
        await WriteProtectedDocumentAsync(
            version: 2,
            new LegacySecretPayload(
                "https://example.supabase.co",
                "test-anon-key",
                "shared@example.com",
                "test-password"));

        using DpapiSecretStore store = new(SecretsPath);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.ReadAsync());

        Assert.Contains("業者コードを導出できません", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReadAsync_NullPayloadRequiresReentry(int version)
    {
        await WriteProtectedDocumentAsync(
            version,
            JsonSerializer.SerializeToUtf8Bytes<object?>(null, SerializerOptions));

        using DpapiSecretStore store = new(SecretsPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());
    }

    [Fact]
    public async Task ReadAsync_UnsupportedVersionRequiresReentry()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(
            SecretsPath,
            "{\"version\":1,\"protectedPayload\":\"\"}");

        using DpapiSecretStore store = new(SecretsPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private Task WriteProtectedDocumentAsync(int version, LegacySecretPayload payload) =>
        WriteProtectedDocumentAsync(
            version,
            JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions));

    private async Task WriteProtectedDocumentAsync(int version, byte[] plainText)
    {
        byte[] cipherText;
        try
        {
            cipherText = ProtectedData.Protect(
                plainText,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            Array.Clear(plainText);
        }

        try
        {
            string document = JsonSerializer.Serialize(
                new SecretDocument(version, Convert.ToBase64String(cipherText)),
                SerializerOptions);
            Directory.CreateDirectory(_temporaryDirectory);
            await File.WriteAllTextAsync(SecretsPath, document);
        }
        finally
        {
            Array.Clear(cipherText);
        }
    }

    private sealed record LegacySecretPayload(
        string ProjectUrl,
        string AnonKey,
        string Email,
        string Password);

    private sealed record SecretDocument(int Version, string ProtectedPayload);
}
