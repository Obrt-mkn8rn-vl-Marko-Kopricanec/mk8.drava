using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayBodyLimitConfigurationTests
{
    // Reserved test names exercise exact host scopes without assigning deployment routes.
    [Theory]
    [InlineData("*.site.test")]
    [InlineData("blob.site.test.")]
    [InlineData("BLOB.site.test")]
    [InlineData("blob.site.test:443")]
    [InlineData("blob")]
    [InlineData("blob..site.test")]
    public void BodyOverridesRefuseAmbiguousOrNoncanonicalScopes(string host)
    {
        var scope = new GatewayRequestBodyLimit { Host = host, MaxRequestBodyBytes = 269_615_107 };
        Assert.Throws<InvalidDataException>(scope.Validate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public void BodyOverridesRemainBounded(long limit)
    {
        var scope = new GatewayRequestBodyLimit { Host = "blob.site.test", MaxRequestBodyBytes = limit };
        Assert.Throws<InvalidDataException>(scope.Validate);
    }

    [Fact]
    public void BootstrapRetainsUnrelatedDefaultAndRefusesDuplicateOrMissingScopes()
    {
        var bootstrap = new GatewayBootstrap
        {
            SiteId = "site", StateDirectory = Path.GetTempPath(),
            Application = OperatingSystem.IsWindows()
                ? new IpcEndpoint { NamedPipeName = "fixture", IdentityTokenPath = Path.Combine(Path.GetTempPath(), "fixture.token") }
                : new IpcEndpoint { UnixSocketPath = Path.Combine(Path.GetTempPath(), "fixture.sock"), IdentityTokenPath = Path.Combine(Path.GetTempPath(), "fixture.token") },
            RequestBodyLimits = [new GatewayRequestBodyLimit { Host = "blob.site.test", MaxRequestBodyBytes = 269_615_107 }],
        };
        bootstrap.Validate();
        Assert.Equal(269_615_107, bootstrap.ResolveRequestBodyLimit("BLOB.site.test"));
        Assert.Equal(100L * 1024 * 1024, bootstrap.ResolveRequestBodyLimit("email.site.test"));
        Assert.Equal(100L * 1024 * 1024, bootstrap.ResolveRequestBodyLimit("other.blob.site.test"));
        Assert.Throws<InvalidDataException>(() => (bootstrap with { RequestBodyLimits = [bootstrap.RequestBodyLimits[0], bootstrap.RequestBodyLimits[0]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (bootstrap with { RequestBodyLimits = null! }).Validate());
        Assert.Throws<InvalidDataException>(() => (bootstrap with { RequestBodyLimits = [null!] }).Validate());
        Assert.Contains("\"requestBodyLimits\"", BootstrapFile.Serialize(bootstrap), StringComparison.Ordinal);
    }
}
