using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class WindowsFileNamePolicyTests
{
    [Theory]
    [InlineData("NUL")]
    [InlineData("nul.txt")]
    [InlineData("NUL.tar.gz")]
    [InlineData("NUL .txt")]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("aux")]
    [InlineData("CONIN$")]
    [InlineData("CONOUT$")]
    [InlineData("COM1")]
    [InlineData("com9.txt")]
    [InlineData("COM¹")]
    [InlineData("COM².txt")]
    [InlineData("COM³.txt")]
    [InlineData("LPT1")]
    [InlineData("LPT9.txt")]
    [InlineData("LPT¹")]
    [InlineData("LPT².txt")]
    [InlineData("LPT³.txt")]
    public void ReservedDosAliasesAreRejectedIncludingExtensionsAndSuperscriptDigits(string name) =>
        Assert.True(WindowsFileNamePolicy.IsDeviceName(name));

    [Theory]
    [InlineData(".env")]
    [InlineData(".installation.key")]
    [InlineData("configuration.json")]
    [InlineData("null.txt")]
    [InlineData("NUL-output")]
    [InlineData("com10")]
    [InlineData("com0")]
    [InlineData("lpt10.txt")]
    [InlineData("lpt0.txt")]
    public void OrdinaryNamesDoNotBecomeDeviceAliases(string name) =>
        Assert.False(WindowsFileNamePolicy.IsDeviceName(name));
}
