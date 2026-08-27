using TachoGraphStudio.Core.Auth;

namespace TachoGraphStudio.Core.Tests.Auth;

public sealed class SupabaseVendorIdentityTests
{
    [Fact]
    public void GetLoginEmail_UsesSharedAccountConvention()
    {
        Assert.Equal(
            "test-vendor@zama-sys.internal",
            SupabaseVendorIdentity.GetLoginEmail("test-vendor"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetLoginEmail_BlankVendorCodeThrows(string vendorCode)
    {
        Assert.Throws<ArgumentException>(() => SupabaseVendorIdentity.GetLoginEmail(vendorCode));
    }

    [Theory]
    [InlineData("test-vendor@zama-sys.internal", "test-vendor")]
    [InlineData("TEST-VENDOR@zama-sys.internal", "TEST-VENDOR")]
    [InlineData("test-vendor@ZAMA-SYS.INTERNAL", "test-vendor")]
    public void TryGetVendorCode_ExtractsCodeFromSharedAccountEmail(
        string email,
        string expectedVendorCode)
    {
        bool result = SupabaseVendorIdentity.TryGetVendorCode(email, out string vendorCode);

        Assert.True(result);
        Assert.Equal(expectedVendorCode, vendorCode);
    }

    [Theory]
    [InlineData("test-vendor")]
    [InlineData("TEST-VENDOR")]
    [InlineData("Test-Vendor")]
    public void TryGetVendorCode_RoundTripsGetLoginEmailWithoutChangingCase(string vendorCode)
    {
        string email = SupabaseVendorIdentity.GetLoginEmail(vendorCode);

        bool result = SupabaseVendorIdentity.TryGetVendorCode(email, out string restored);

        Assert.True(result);
        Assert.Equal(vendorCode, restored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("shared@example.com")]
    [InlineData("test-vendor@other.internal")]
    public void TryGetVendorCode_RejectsUnknownEmail(string email)
    {
        bool result = SupabaseVendorIdentity.TryGetVendorCode(email, out string vendorCode);

        Assert.False(result);
        Assert.Empty(vendorCode);
    }
}
