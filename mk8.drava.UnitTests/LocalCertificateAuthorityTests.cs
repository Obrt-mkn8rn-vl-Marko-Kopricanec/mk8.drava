using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class LocalCertificateAuthorityTests
{
    [Fact]
    public async Task ExplicitInitializationCreatesPrivatePinnedRootAndScopedLeavesAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var clock = new RegistryTimeProvider();
        using var created = LocalSiteCertificateAuthority.Create("site", clock);
        var fingerprint = created.Fingerprint;
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new ControllerBootstrap { CertificateAuthority = created.PrivateMaterial, EnrollmentRootFingerprint = fingerprint }), CancellationToken.None).ConfigureAwait(true);
        var configured = await PortableConfiguration.LoadDocumentAsync<ControllerBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        using var authority = LocalSiteCertificateAuthority.Open(configured.CertificateAuthority, fingerprint, clock);
        using var root = authority.PublicCertificate;
        Assert.False(root.HasPrivateKey);
        Assert.Equal(fingerprint, root.GetCertHashString(HashAlgorithmName.SHA256));
        using var gateway = authority.IssueGateway("site.example", ["127.0.0.1"]);
        using var node = authority.IssueNode("node", ["127.0.0.1"]);
        Assert.True(gateway.HasPrivateKey);
        Assert.True(node.HasPrivateKey);
        var gatewayUsage = GetUsage(gateway);
        Assert.Contains(gatewayUsage, usage => string.Equals(usage.Value, "1.3.6.1.5.5.7.3.1", StringComparison.Ordinal));
        Assert.DoesNotContain(gatewayUsage, usage => string.Equals(usage.Value, "1.3.6.1.5.5.7.3.2", StringComparison.Ordinal));
        Assert.Contains(GetUsage(node), usage => string.Equals(usage.Value, "1.3.6.1.5.5.7.3.2", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(directory.Path, "site-ca.pfx")));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task ProtectedTrustIsRetainedAcrossRuntimeOpenAndCannotIgnoreItsPinAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var clock = new RegistryTimeProvider();
        using var created = LocalSiteCertificateAuthority.Create("site", clock);
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new ControllerBootstrap { CertificateAuthority = created.PrivateMaterial, EnrollmentRootFingerprint = created.Fingerprint }), CancellationToken.None).ConfigureAwait(true);
        var first = await PortableConfiguration.LoadDocumentAsync<ControllerBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        Assert.Throws<InvalidDataException>(() => LocalSiteCertificateAuthority.Open(first.CertificateAuthority, RegistryTestFixture.Fingerprint, clock));
        var reopened = await PortableConfiguration.LoadDocumentAsync<ControllerBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        using var unchanged = LocalSiteCertificateAuthority.Open(reopened.CertificateAuthority, reopened.EnrollmentRootFingerprint, clock);
        Assert.Equal(created.Fingerprint, unchanged.Fingerprint);
        Assert.Equal(first.CertificateAuthority, reopened.CertificateAuthority);
        Assert.False(File.Exists(Path.Combine(directory.Path, "site-ca.pfx")));
    }

    private static List<Oid> GetUsage(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509EnhancedKeyUsageExtension usages)
            {
                List<Oid> result = [];
                foreach (Oid usage in usages.EnhancedKeyUsages) result.Add(usage);
                return result;
            }
        }

        throw new InvalidOperationException("Fixture certificate has no EKU.");
    }
}
