using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.INF.Publication;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class LocalCertificateAuthorityTests
{
    [Fact]
    public async Task ExplicitInitializationCreatesPrivatePinnedRootAndScopedLeavesAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "site-ca.pfx");
        var clock = new RegistryTimeProvider();
        var fingerprint = await LocalSiteCertificateAuthority.InitializeAsync(path, "site", clock, CancellationToken.None).ConfigureAwait(true);
        using var authority = LocalSiteCertificateAuthority.Open(path, fingerprint, clock);
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
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task InitializationCannotReplaceExistingTrustOrIgnoreItsPinAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "site-ca.pfx");
        var clock = new RegistryTimeProvider();
        var fingerprint = await LocalSiteCertificateAuthority.InitializeAsync(path, "site", clock, CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<IOException>(async () => await LocalSiteCertificateAuthority.InitializeAsync(path, "site", clock, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Throws<InvalidDataException>(() => LocalSiteCertificateAuthority.Open(path, RegistryTestFixture.Fingerprint, clock));
        using var unchanged = LocalSiteCertificateAuthority.Open(path, fingerprint, clock);
        Assert.Equal(fingerprint, unchanged.Fingerprint);
    }

    private static List<Oid> GetUsage(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
            if (extension is X509EnhancedKeyUsageExtension usages)
            {
                List<Oid> result = [];
                foreach (Oid usage in usages.EnhancedKeyUsages) result.Add(usage);
                return result;
            }
        throw new InvalidOperationException("Fixture certificate has no EKU.");
    }
}
