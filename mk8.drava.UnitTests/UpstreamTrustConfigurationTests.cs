using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;
using Mk8.Drava.Application.INF.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpstreamTrustConfigurationTests
{
    [Theory]
    [InlineData("/root.cer", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("root with spaces", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("root", "lowercase")]
    [InlineData("root", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void TrustPolicyRejectsNoncanonicalOperatorInputs(string materialId, string fingerprint)
    {
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        Assert.Throws<ArgumentException>(() => new RuntimeTrustedRootCertificate(materialId, fingerprint, certificates.Root.RawData));
    }

    [Theory]
    [InlineData("http", true)]
    [InlineData("https", false)]
    public void AConfiguredRootRequiresVerifiedHttps(string scheme, bool validateCertificate)
    {
        var options = Options(scheme, validateCertificate);
        var result = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new ProxyUrlSyntaxPolicy()).Validate(name: null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, static failure => failure.Contains("TrustedRoot requires", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeMappingRetainsTrustAndDifferentPinsCannotSharePools()
    {
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var fingerprint = certificates.Root.GetCertHashString(HashAlgorithmName.SHA256);
        var configured = new RuntimeTrustedRootCertificate("operator-root", fingerprint, certificates.Root.RawData);
        var options = Options("https", validateCertificate: true, fingerprint);
        var routes = ProxyConfigurationRuntimeMapper.ToRuntimeRoutes(options.Routes, new ProxyOperationalOptions(), Source(configured));
        var upstream = Only(Only(routes).Upstreams);
        var root = Assert.IsType<RuntimeTrustedRootCertificate>(upstream.Tls.TrustedRoot);
        Assert.Equal("operator-root", root.MaterialId);
        Assert.Equal(fingerprint, root.Sha256);
        var first = UpstreamTransportEndpointMapper.FromUpstream(upstream);
        var second = first with { TrustedRoot = new RuntimeTrustedRootCertificate(root.MaterialId, new string('B', 64), certificates.Root.RawData) };
        var otherMaterial = first with { TrustedRoot = new RuntimeTrustedRootCertificate("another-root", root.Sha256, certificates.Root.RawData) };
        Assert.False(string.Equals(first.PoolKey, second.PoolKey, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(first.PoolKey, otherMaterial.PoolKey, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(first.PoolKey, (first with { TrustedRoot = null }).PoolKey, StringComparison.OrdinalIgnoreCase));
        var caseChange = first with { TrustedRoot = new RuntimeTrustedRootCertificate("Operator-root", root.Sha256, certificates.Root.RawData) };
        Assert.False(string.Equals(first.PoolKey, caseChange.PoolKey, StringComparison.OrdinalIgnoreCase));
        Assert.Throws<ArgumentException>(() => new RuntimeUpstreamTlsOptions(ValidateCertificate: false, "backend.drava.invalid", root));
        Assert.Throws<ArgumentException>(() => new RuntimeUpstream("service", "peer", "http", "http1", "127.0.0.1", 8080, 1, upstream.Tls));
        var response = RuntimeUpstreamTlsResponseMapper.FromProjection(new RuntimeUpstreamTlsProjection(ValidateCertificate: true, upstream.Tls.SniHost, root));
        Assert.Equal(root.MaterialId, response.TrustedRoot?.MaterialId);
        Assert.Equal(root.Sha256, response.TrustedRoot?.Sha256);
    }

    [Fact]
    public void ConfiguredRootsRequireTheirStartupMaterialResolver()
    {
        var options = Options("https", validateCertificate: true);
        Assert.Throws<InvalidDataException>(() => ProxyConfigurationRuntimeMapper.ToRuntimeRoutes(options.Routes, new ProxyOperationalOptions()));
    }

    [Fact]
    public void RuntimeTrustOwnsTheInputAndEveryReturnedCopy()
    {
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var bytes = certificates.Root.RawData;
        var root = new RuntimeTrustedRootCertificate("operator-root", certificates.Root.GetCertHashString(HashAlgorithmName.SHA256), bytes);
        bytes[0] ^= 1;
        var copy = root.CopyCertificate();
        copy[0] ^= 1;
        Assert.Equal(certificates.Root.RawData, root.CopyCertificate());
    }

    [Fact]
    public void PortableAdapterRetainsItsStartupSnapshotWhenInputsAndReadCopiesChange()
    {
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        using var replacement = DevelopmentUpstreamTrustCertificates.Create();
        var bytes = certificates.Root.RawData;
        var entries = new List<UpstreamTrustMaterial> { new() { Id = "operator-root", Certificate = new SecretBytes(bytes) } };
        var source = new PortableUpstreamTrustMaterialSource(new ApplicationBootstrap { UpstreamTrust = entries });
        var fingerprint = certificates.Root.GetCertHashString(HashAlgorithmName.SHA256);
        bytes[0] ^= 1;
        entries[0] = new UpstreamTrustMaterial { Id = "replacement", Certificate = new SecretBytes(replacement.Root.RawData) };
        var returned = source.Resolve("operator-root", fingerprint).CopyCertificate();
        returned[0] ^= 1;
        Assert.Equal(certificates.Root.RawData, source.Resolve("operator-root", fingerprint).CopyCertificate());
        Assert.Throws<InvalidDataException>(() => source.Resolve("replacement", replacement.Root.GetCertHashString(HashAlgorithmName.SHA256)));
    }

    [Fact]
    public void PortableAdapterRejectsMissingMaterialAndChangedPins()
    {
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var source = new PortableUpstreamTrustMaterialSource(new ApplicationBootstrap
        {
            UpstreamTrust = [new UpstreamTrustMaterial { Id = "operator-root", Certificate = new SecretBytes(certificates.Root.RawData) }],
        });
        Assert.Throws<InvalidDataException>(() => source.Resolve("missing", certificates.Root.GetCertHashString(HashAlgorithmName.SHA256)));
        Assert.Throws<InvalidDataException>(() => source.Resolve("operator-root", new string('0', 64)));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("leaf")]
    [InlineData("invalid-bytes")]
    [InlineData("private-pfx")]
    public void PortableAdapterRejectsAmbiguousOrInvalidAuthorities(string fault)
    {
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var bytes = fault switch
        {
            "leaf" => certificates.Leaf.RawData,
            "invalid-bytes" => new byte[128],
            "private-pfx" => certificates.Leaf.Export(X509ContentType.Pkcs12),
            "duplicate" => certificates.Root.RawData,
            _ => throw new InvalidOperationException("Unknown synthetic trust corruption."),
        };
        var material = new UpstreamTrustMaterial { Id = "operator-root", Certificate = new SecretBytes(bytes) };
        CryptographicOperations.ZeroMemory(bytes);
        var entries = new List<UpstreamTrustMaterial> { material };
        if (string.Equals(fault, "duplicate", StringComparison.Ordinal)) entries.Add(material);
        Assert.Throws<InvalidDataException>(() => new PortableUpstreamTrustMaterialSource(new ApplicationBootstrap { UpstreamTrust = entries }));
    }

    [Theory]
    [InlineData(SiteConfigurationFormat.Json)]
    [InlineData(SiteConfigurationFormat.Yaml)]
    public void RetiredTrustPathsCannotBeSilentlyIgnoredInSiteDocuments(SiteConfigurationFormat format)
    {
        var document = format == SiteConfigurationFormat.Json
            ? """
              {"routes":[{"upstreams":[{"upstreamTls":{"trustedRoot":{"certificatePath":"/retired/root.cer","materialId":"operator-root","sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}}}]}]}
              """
            : """
              routes:
                - upstreams:
                    - upstreamTls:
                        trustedRoot:
                          certificatePath: /retired/root.cer
                          materialId: operator-root
                          sha256: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
              """;
        var parser = new SiteConfigurationParser();
        Assert.Throws<JsonException>(() => parser.ReadSiteText(document, format));
    }

    [Fact]
    public void RetiredTrustPathsCannotBeSilentlyIgnoredInAutomaticPolicies()
    {
        const string document = """
            {"site":{"upstreamTls":{"trustedRoot":{"certificatePath":"/retired/root.cer","materialId":"operator-root","sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}}}}
            """;
        Assert.Throws<JsonException>(() => NoConfPolicyFile.Parse(document));
    }

    internal static IUpstreamTrustMaterialSource Source(RuntimeTrustedRootCertificate material) => new PortableUpstreamTrustMaterialSource(new ApplicationBootstrap
    {
        UpstreamTrust = [new UpstreamTrustMaterial { Id = material.MaterialId, Certificate = new SecretBytes(material.CopyCertificate()) }],
    });

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "xUnit Assert.Single verifies exactly one item; LINQ First would remove the cardinality assertion.")]
    internal static T Only<T>(IReadOnlyList<T> items) => Assert.Single(items);

    private static ProxyOptions Options(string scheme, bool validateCertificate, string? fingerprint = null) => new()
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
                    TrustedRoot = new TrustedRootCertificateOptions { MaterialId = "operator-root", Sha256 = fingerprint ?? new string('A', 64) },
                },
            }],
        }],
    };
}
