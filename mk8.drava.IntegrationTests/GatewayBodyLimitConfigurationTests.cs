using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class GatewayBodyLimitConfigurationTests
{
    [Theory]
    [InlineData("*.mk8n.com")]
    [InlineData("blob.mk8n.com.")]
    [InlineData("BLOB.mk8n.com")]
    [InlineData("blob.mk8n.com:443")]
    [InlineData("blob")]
    [InlineData("blob..mk8n.com")]
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
        var scope = new GatewayRequestBodyLimit { Host = "blob.mk8n.com", MaxRequestBodyBytes = limit };
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
            RequestBodyLimits = [new GatewayRequestBodyLimit { Host = "blob.mk8n.com", MaxRequestBodyBytes = 269_615_107 }],
        };
        bootstrap.Validate();
        Assert.Equal(269_615_107, bootstrap.ResolveRequestBodyLimit("BLOB.mk8n.com"));
        Assert.Equal(100L * 1024 * 1024, bootstrap.ResolveRequestBodyLimit("email.mk8n.com"));
        Assert.Equal(100L * 1024 * 1024, bootstrap.ResolveRequestBodyLimit("other.blob.mk8n.com"));
        Assert.Throws<InvalidDataException>(() => (bootstrap with { RequestBodyLimits = [bootstrap.RequestBodyLimits[0], bootstrap.RequestBodyLimits[0]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (bootstrap with { RequestBodyLimits = null! }).Validate());
        Assert.Throws<InvalidDataException>(() => (bootstrap with { RequestBodyLimits = [null!] }).Validate());
        Assert.Contains("\"requestBodyLimits\"", BootstrapFile.Serialize(bootstrap), StringComparison.Ordinal);
    }
}
