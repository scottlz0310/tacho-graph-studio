using TachoGraphStudio.Core.Naming;

namespace TachoGraphStudio.Core.Tests.Naming;

public sealed class OutputNamingTests
{
    [Theory]
    [InlineData("2026/12/25", "旭川123-45", "山田 太郎", false, "20261225_旭川123-45_山田 太郎.png")]
    [InlineData("2026/12/25", "旭川123-45", "山田 太郎", true, "20261225_旭川123-45_山田 太郎_手書き.png")]
    [InlineData("2026-12-25", "旭川123-45", "山田 太郎", false, "20261225_旭川123-45_山田 太郎.png")]
    [InlineData("2026.12.25", "旭川123-45", "山田 太郎", false, "20261225_旭川123-45_山田 太郎.png")]
    [InlineData(" 2026/12/25 ", "旭川123-45", "山田 太郎", false, "20261225_旭川123-45_山田 太郎.png")]
    [InlineData("R8/12/25", "旭川123-45", "山田 太郎", false, "R81225_旭川123-45_山田 太郎.png")]
    [InlineData("", "", "", false, "__.png")]
    public void CreateFileName_FormatsDateRegistrationAndDriver(
        string printDate,
        string registrationNumber,
        string driverName,
        bool skipHandwritten,
        string expected)
    {
        string fileName = OutputNaming.CreateFileName(
            printDate, registrationNumber, driverName, skipHandwritten);

        Assert.Equal(expected, fileName);
    }

    [Theory]
    [InlineData("2026/12/25", "旭川<123>", "山田/太郎", false, "20261225_旭川_123__山田_太郎.png")]
    [InlineData("2026:12:25", "旭川123-45", "山田 太郎", false, "2026_12_25_旭川123-45_山田 太郎.png")]
    [InlineData("2026/12/25", "旭川<123>", "山田/太郎", true, "20261225_旭川_123__山田_太郎_手書き.png")]
    public void CreateFileName_SanitizesInvalidFileNameChars(
        string printDate,
        string registrationNumber,
        string driverName,
        bool skipHandwritten,
        string expected)
    {
        string fileName = OutputNaming.CreateFileName(
            printDate, registrationNumber, driverName, skipHandwritten);

        Assert.Equal(expected, fileName);
    }
}
