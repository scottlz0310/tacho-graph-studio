using TachoGraphStudio.Core.Settings;

namespace TachoGraphStudio.Core.Tests.Settings;

public sealed class SupabaseCredentialsTests
{
    [Theory]
    [InlineData("https://example.supabase.co")]
    [InlineData("https://example.supabase.co/")]
    public void Create_ValidHttpsUrlAndAnonKeySucceeds(string projectUrl)
    {
        SupabaseCredentials credentials = SupabaseCredentials.Create(new Uri(projectUrl), "test-anon-key");

        Assert.Equal(new Uri(projectUrl), credentials.ProjectUrl);
        Assert.Equal("test-anon-key", credentials.AnonKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankAnonKeyThrows(string anonKey)
    {
        Assert.Throws<ArgumentException>(
            () => SupabaseCredentials.Create(new Uri("https://example.supabase.co"), anonKey));
    }

    [Theory]
    [InlineData("http://example.supabase.co")]
    [InlineData("ftp://example.supabase.co")]
    public void Create_NonHttpsSchemeThrows(string projectUrl)
    {
        Assert.Throws<ArgumentException>(
            () => SupabaseCredentials.Create(new Uri(projectUrl), "test-anon-key"));
    }

    [Fact]
    public void Create_RelativeUriThrows()
    {
        Uri relativeUri = new("/relative/path", UriKind.Relative);

        Assert.Throws<ArgumentException>(() => SupabaseCredentials.Create(relativeUri, "test-anon-key"));
    }

    [Fact]
    public void Create_NullProjectUrlThrows()
    {
        Assert.Throws<ArgumentNullException>(() => SupabaseCredentials.Create(null!, "test-anon-key"));
    }
}
