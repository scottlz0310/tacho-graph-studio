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
            "test-vendor",
            "test-password");

        Assert.Equal(new Uri(projectUrl), credentials.ProjectUrl);
        Assert.Equal("test-anon-key", credentials.AnonKey);
        Assert.Equal("test-vendor", credentials.VendorCode);
        Assert.Equal("test-vendor@zama-sys.internal", credentials.Email);
        Assert.Equal("test-password", credentials.Password);
    }

    [Theory]
    [InlineData("", "test-vendor", "test-password")]
    [InlineData("   ", "test-vendor", "test-password")]
    [InlineData("test-anon-key", "", "test-password")]
    [InlineData("test-anon-key", "   ", "test-password")]
    [InlineData("test-anon-key", "test-vendor", "")]
    [InlineData("test-anon-key", "test-vendor", "   ")]
    public void Create_BlankValueThrows(string anonKey, string vendorCode, string password)
    {
        Assert.Throws<ArgumentException>(
            () => SupabaseCredentials.Create(
                new Uri("https://example.supabase.co"),
                anonKey,
                vendorCode,
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
                "test-vendor",
                "test-password"));
    }

    [Fact]
    public void Create_RelativeUriThrows()
    {
        Uri relativeUri = new("/relative/path", UriKind.Relative);

        Assert.Throws<ArgumentException>(() => SupabaseCredentials.Create(
            relativeUri,
            "test-anon-key",
            "test-vendor",
            "test-password"));
    }

    [Fact]
    public void Create_NullProjectUrlThrows()
    {
        Assert.Throws<ArgumentNullException>(() => SupabaseCredentials.Create(
            null!,
            "test-anon-key",
            "test-vendor",
            "test-password"));
    }
}
