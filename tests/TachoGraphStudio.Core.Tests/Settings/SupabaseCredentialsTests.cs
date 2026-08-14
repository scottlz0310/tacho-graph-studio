using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.Core.Tests.Settings;

public sealed class SupabaseCredentialsTests
{
    [Theory]
    [InlineData("https://example.supabase.co")]
    [InlineData("https://example.supabase.co/")]
    public void Create_ValidHttpsUrlAndCredentialsSucceeds(string projectUrl)
    {
        SupabaseCredentials credentials = SupabaseCredentials.Create(
            new Uri(projectUrl),
            "test-anon-key",
            "shared@example.com",
            "test-password");

        Assert.Equal(new Uri(projectUrl), credentials.ProjectUrl);
        Assert.Equal("test-anon-key", credentials.AnonKey);
        Assert.Equal("shared@example.com", credentials.Email);
        Assert.Equal("test-password", credentials.Password);
    }

    [Theory]
    [InlineData("", "shared@example.com", "test-password")]
    [InlineData("   ", "shared@example.com", "test-password")]
    [InlineData("test-anon-key", "", "test-password")]
    [InlineData("test-anon-key", "   ", "test-password")]
    [InlineData("test-anon-key", "shared@example.com", "")]
    [InlineData("test-anon-key", "shared@example.com", "   ")]
    public void Create_BlankValueThrows(string anonKey, string email, string password)
    {
        Assert.Throws<ArgumentException>(
            () => SupabaseCredentials.Create(
                new Uri("https://example.supabase.co"),
                anonKey,
                email,
                password));
    }

    [Theory]
    [InlineData("http://example.supabase.co")]
    [InlineData("ftp://example.supabase.co")]
    public void Create_NonHttpsSchemeThrows(string projectUrl)
    {
        Assert.Throws<ArgumentException>(
            () => SupabaseCredentials.Create(
                new Uri(projectUrl),
                "test-anon-key",
                "shared@example.com",
                "test-password"));
    }

    [Fact]
    public void Create_RelativeUriThrows()
    {
        Uri relativeUri = new("/relative/path", UriKind.Relative);

        Assert.Throws<ArgumentException>(() => SupabaseCredentials.Create(
            relativeUri,
            "test-anon-key",
            "shared@example.com",
            "test-password"));
    }

    [Fact]
    public void Create_NullProjectUrlThrows()
    {
        Assert.Throws<ArgumentNullException>(() => SupabaseCredentials.Create(
            null!,
            "test-anon-key",
            "shared@example.com",
            "test-password"));
    }
}
