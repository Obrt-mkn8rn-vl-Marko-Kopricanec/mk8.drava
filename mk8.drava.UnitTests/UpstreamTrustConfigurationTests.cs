using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;
using Mk8.Drava.Application.INF.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpstreamTrustConfigurationTests
{
    [Theory]
    [InlineData("relative.cer", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("/root.cer", "lowercase")]
    [InlineData("/root.cer", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void TrustPolicyRejectsNoncanonicalOperatorInputs(string path, string fingerprint)
    {
        Assert.Throws<ArgumentException>(() => new RuntimeTrustedRootCertificate(path, fingerprint));
    }

    [Theory]
    [InlineData("http", true)]
    [InlineData("https", false)]
    public void AConfiguredRootRequiresVerifiedHttps(string scheme, bool validateCertificate)
    {
        var options = Options(scheme, validateCertificate);
        var result = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy()).Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, static failure => failure.Contains("TrustedRoot requires", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeMappingRetainsTrustAndDifferentPinsCannotSharePools()
    {
        var options = Options("https", validateCertificate: true);
        var routes = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes(options.Routes, new ProxyOperationalOptions());
        var upstream = Only(Only(routes).Upstreams);
        var root = Assert.IsType<RuntimeTrustedRootCertificate>(upstream.Tls.TrustedRoot);
        Assert.Equal(Path.GetFullPath("operator-root.cer"), root.CertificatePath);
        Assert.Equal(new string('A', 64), root.Sha256);
        var first = UpstreamTransportEndpointMapper.FromUpstream(upstream);
        var second = first with { TrustedRoot = new RuntimeTrustedRootCertificate(root.CertificatePath, new string('B', 64)) };
        var otherPath = first with { TrustedRoot = new RuntimeTrustedRootCertificate(Path.GetFullPath("another-root.cer"), root.Sha256) };
        Assert.False(string.Equals(first.PoolKey, second.PoolKey, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(first.PoolKey, otherPath.PoolKey, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(first.PoolKey, (first with { TrustedRoot = null }).PoolKey, StringComparison.OrdinalIgnoreCase));
        var caseChange = first with { TrustedRoot = new RuntimeTrustedRootCertificate(Path.GetFullPath("Operator-root.cer"), root.Sha256) };
        Assert.False(string.Equals(first.PoolKey, caseChange.PoolKey, StringComparison.OrdinalIgnoreCase));
        Assert.Throws<ArgumentException>(() => new RuntimeUpstreamTlsOptions(false, "backend.drava.invalid", root));
        Assert.Throws<ArgumentException>(() => new RuntimeUpstream("service", "peer", "http", "http1", "127.0.0.1", 8080, 1, upstream.Tls));
        var response = RuntimeUpstreamTlsResponseMapper.FromProjection(new RuntimeUpstreamTlsProjection(true, upstream.Tls.SniHost, root));
        Assert.Equal(root.CertificatePath, response.TrustedRoot?.CertificatePath);
        Assert.Equal(root.Sha256, response.TrustedRoot?.Sha256);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "xUnit Assert.Single verifies exactly one item; LINQ First would remove the cardinality assertion.")]
    private static T Only<T>(IReadOnlyList<T> items) => Assert.Single(items);

    private static ProxyOptions Options(string scheme, bool validateCertificate) => new()
    {
        Listeners = [new ListenerOptions { Name = "http", Address = "127.0.0.1", Port = 8080 }],
        Routes = [new ProxyRouteOptions
        {
            Name = "service", Host = "service.drava.invalid",
            Upstreams = [new UpstreamOptions
            {
                Name = "peer", Scheme = scheme, Address = "127.0.0.1", Port = 8443,
                UpstreamTls = new UpstreamTlsOptions
                {
                    ValidateCertificate = validateCertificate, SniHost = "backend.drava.invalid",
                    TrustedRoot = new TrustedRootCertificateOptions { CertificatePath = Path.GetFullPath("operator-root.cer"), Sha256 = new string('A', 64) },
                },
            }],
        }],
    };
}
