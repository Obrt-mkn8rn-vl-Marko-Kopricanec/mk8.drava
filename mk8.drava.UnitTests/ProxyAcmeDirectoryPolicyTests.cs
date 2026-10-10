using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProxyAcmeDirectoryPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnvironmentChoiceNeverInventsADirectoryEndpoint(bool useStaging)
    {
        var acme = new ProxyAcmeOptions { UseStaging = useStaging };
        Assert.Empty(ProxyAcmeDirectoryPolicy.ResolveDirectoryUrl(acme));
        Assert.Empty(Validate(acme));
        var enabled = new ProxyAcmeOptions { Enabled = true, UseStaging = useStaging };
        Assert.Contains(Validate(enabled), static failure => failure.Contains("DirectoryUrl", StringComparison.Ordinal));
        var configured = new ProxyAcmeOptions { Enabled = true, UseStaging = useStaging, DirectoryUrl = " https://ca.invalid/directory " };
        Assert.Equal("https://ca.invalid/directory", ProxyAcmeDirectoryPolicy.ResolveDirectoryUrl(configured));
        Assert.Empty(Validate(configured));
    }

    private static IReadOnlyList<string> Validate(ProxyAcmeOptions acme) => ProxyOperationalOptionsValidationRules.Validate(
        new ProxyOperationalOptions { Acme = acme }, static _ => null, new ProxyAdminUrlPolicy(), new ProxyRelativeStoragePathPolicy(),
        new ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy());
}
