using TachoGraphStudio.Core.Roster;

namespace TachoGraphStudio.Core.Tests.Roster;

public sealed class LoginVendorTests
{
    [Theory]
    [InlineData("株式会社テスト", "株式会社テスト")]
    [InlineData(null, "test-vendor")]
    [InlineData("", "test-vendor")]
    [InlineData("   ", "test-vendor")]
    public void DisplayLabel_FallsBackToCodeWhenDisplayNameIsBlank(
        string? displayName,
        string expected)
    {
        LoginVendor vendor = new()
        {
            Code = "test-vendor",
            DisplayName = displayName!,
            SortOrder = 1,
        };

        Assert.Equal(expected, vendor.DisplayLabel);
    }
}
