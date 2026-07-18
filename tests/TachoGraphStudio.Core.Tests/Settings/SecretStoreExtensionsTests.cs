using System.Security.Cryptography;

using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.Core.Tests.Settings;

public sealed class SecretStoreExtensionsTests
{
    private static readonly SupabaseCredentials Credentials = SupabaseCredentials.Create(
        new Uri("https://example.supabase.co"),
        "test-anon-key");

    [Fact]
    public async Task TryWriteAsync_StoreSucceedsReturnsTrue()
    {
        RecordingSecretStore store = new();

        bool result = await store.TryWriteAsync(Credentials, CancellationToken.None);

        Assert.True(result);
        Assert.Same(Credentials, store.LastWritten);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(CryptographicException))]
    public async Task TryWriteAsync_StoreThrowsReturnsFalseWithoutPropagating(Type exceptionType)
    {
        ThrowingSecretStore store = new((Exception)Activator.CreateInstance(exceptionType)!);

        bool result = await store.TryWriteAsync(Credentials, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryWriteAsync_CancellationPropagates()
    {
        ThrowingSecretStore store = new(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.TryWriteAsync(Credentials, CancellationToken.None));
    }

    private sealed class RecordingSecretStore : ISecretStore
    {
        public SupabaseCredentials? LastWritten { get; private set; }

        public Task<SupabaseCredentials?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LastWritten);

        public Task WriteAsync(SupabaseCredentials credentials, CancellationToken cancellationToken = default)
        {
            LastWritten = credentials;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSecretStore(Exception exception) : ISecretStore
    {
        public Task<SupabaseCredentials?> ReadAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task WriteAsync(SupabaseCredentials credentials, CancellationToken cancellationToken = default) =>
            throw exception;
    }
}
