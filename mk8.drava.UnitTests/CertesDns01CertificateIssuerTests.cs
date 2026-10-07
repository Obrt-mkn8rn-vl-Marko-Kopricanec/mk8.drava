using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.INF.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class CertesDns01CertificateIssuerTests
{
    [Fact]
    public async Task SignedOrderUsesApprovedCsrAndReturnsThePrivateLeafAndIssuerChainAsync()
    {
        using var fixture = new IssuerFixture();
        using var issuer = fixture.CreateIssuer();
        var result = Assert.IsType<AcmeCertificateIssueResult.IssuedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        var certificates = X509CertificateLoader.LoadPkcs12Collection(result.PfxBytes, string.Empty, X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            var privateLeaves = certificates.Cast<X509Certificate2>().Where(static c => c.HasPrivateKey).ToArray();
            Assert.Collection(privateLeaves, static c => Assert.True(c.HasPrivateKey));
            var leaf = privateLeaves[0];
            Assert.Equal(2, certificates.Count);
            Assert.Collection(fixture.Servers, static server => Assert.NotEmpty(server.RootCertificate));
            using var root = X509CertificateLoader.LoadCertificate(fixture.Servers[0].RootCertificate);
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.ExtraStore.AddRange(certificates);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            Assert.True(chain.Build(leaf));
            Assert.Equal(3, chain.ChainElements.Count);
            Assert.Equal(2, fixture.Dns.Published); Assert.Equal(2, fixture.Dns.Removed); Assert.Empty(fixture.Dns.Records);
            Assert.Equal(2, fixture.Servers[0].Validations); Assert.Equal(1, fixture.Servers[0].Finalizations);
        }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }

    [Fact]
    public async Task AccountKeyIsProtectedAndReusedAcrossIndependentIssuerInstancesAsync()
    {
        using var fixture = new IssuerFixture();
        byte[] first;
        using (var issuer = fixture.CreateIssuer())
        {
            Assert.IsType<AcmeCertificateIssueResult.IssuedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
            first = await File.ReadAllBytesAsync(fixture.Policy.AccountKeyPath).ConfigureAwait(true);
        }
        using (var issuer = fixture.CreateIssuer())
            Assert.IsType<AcmeCertificateIssueResult.IssuedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(first, await File.ReadAllBytesAsync(fixture.Policy.AccountKeyPath).ConfigureAwait(true));
        Assert.Equal(2, fixture.Servers.Count);
        Assert.Equal(fixture.Servers[0].AccountPublicKey, fixture.Servers[1].AccountPublicKey);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.Policy.AccountKeyPath));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("domain")]
    [InlineData("contacts")]
    [InlineData("terms")]
    public async Task RequestCannotChangeOwnerIssuerAuthorityBeforeNetworkAccessAsync(string mismatch)
    {
        using var fixture = new IssuerFixture();
        using var issuer = fixture.CreateIssuer();
        var request = new AcmeCertificateIssueRequest("site", string.Equals(mismatch, "domain", StringComparison.Ordinal) ? ["foreign.example"] : ["site.example", "*.site.example"],
            string.Equals(mismatch, "directory", StringComparison.Ordinal) ? "https://foreign.example/directory" : fixture.Policy.Directory.AbsoluteUri,
            string.Equals(mismatch, "contacts", StringComparison.Ordinal) ? ["foreign@example.org"] : fixture.Policy.ContactEmails, !string.Equals(mismatch, "terms", StringComparison.Ordinal));
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(request, new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(fixture.Servers); Assert.False(File.Exists(fixture.Policy.AccountKeyPath)); Assert.Empty(fixture.Dns.Records);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignServerIdentifiersAreRejectedBeforeChallengePublicationAsync(bool order)
    {
        using var fixture = new IssuerFixture();
        using var issuer = fixture.CreateIssuer(order ? "order" : "authorization");
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(0, fixture.Dns.Published); Assert.Equal(0, fixture.Servers[0].Finalizations);
    }

    [Fact]
    public async Task InvalidAuthorizationCleansOnlyItsPublishedRecordAndDoesNotFinalizeAsync()
    {
        using var fixture = new IssuerFixture();
        using var issuer = fixture.CreateIssuer("rejected");
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, fixture.Dns.Published); Assert.Equal(1, fixture.Dns.Removed); Assert.Empty(fixture.Dns.Records);
        Assert.Equal(0, fixture.Servers[0].Finalizations);
    }

    [Fact]
    public async Task MissingPropagationTimesOutWithoutNotifyingTheCaAndJoinsCleanupAsync()
    {
        using var fixture = new IssuerFixture(new DevelopmentAcmeDnsProvider { MissingProof = true });
        using var issuer = fixture.CreateIssuer(policy: fixture.Policy with { OperationTimeout = TimeSpan.FromSeconds(1) });
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, fixture.Dns.Published); Assert.Equal(1, fixture.Dns.Removed); Assert.Empty(fixture.Dns.Records);
        Assert.Equal(0, fixture.Servers[0].Validations); Assert.Equal(0, fixture.Servers[0].Finalizations);
    }

    [Fact]
    public async Task OwnerCancellationJoinsBlockedDnsProofAndCleanupAndConcurrentOrderIsRejectedAsync()
    {
        using var fixture = new IssuerFixture(new DevelopmentAcmeDnsProvider { BlockProof = true });
        using var issuer = fixture.CreateIssuer();
        using var cancellation = new CancellationTokenSource();
        var issue = Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), cancellation.Token).ConfigureAwait(false);
        });
        try
        {
            await fixture.Dns.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
            Assert.Collection(fixture.Servers, static server => Assert.True(server.Requests > 0));
        }
        finally { await cancellation.CancelAsync().ConfigureAwait(true); await issue.ConfigureAwait(true); }
        Assert.True(fixture.Dns.Exited.Task.IsCompletedSuccessfully); Assert.Equal(1, fixture.Dns.Removed); Assert.Empty(fixture.Dns.Records);
        Assert.Equal(0, fixture.Servers[0].Validations);
    }

    [Fact]
    public async Task UnconfirmedCleanupPreventsCertificateResultAsync()
    {
        using var fixture = new IssuerFixture(new DevelopmentAcmeDnsProvider { FailCleanup = true });
        using var issuer = fixture.CreateIssuer();
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(0, fixture.Servers[0].Finalizations); Assert.Collection(fixture.Dns.Records, static record => Assert.Equal("_acme-challenge.site.example", record.Host));
    }

    [Fact]
    public async Task ExistingInvalidAccountMaterialIsRetainedAndNoNetworkOperationRunsAsync()
    {
        using var fixture = new IssuerFixture();
        var bytes = new byte[256]; await File.WriteAllBytesAsync(fixture.Policy.AccountKeyPath, bytes).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture.Policy.AccountKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var issuer = fixture.CreateIssuer();
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Policy.AccountKeyPath).ConfigureAwait(true)); Assert.Empty(fixture.Servers);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("different-curve")]
    [InlineData("multiple")]
    public async Task ExistingAccountRequiresOnePrivateP256KeyAndIsNeverRegeneratedAsync(string invalid)
    {
        using var fixture = new IssuerFixture();
        using var key = ECDsa.Create(string.Equals(invalid, "different-curve", StringComparison.Ordinal) ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256);
        var pem = string.Equals(invalid, "public", StringComparison.Ordinal) ? key.ExportSubjectPublicKeyInfoPem() : key.ExportPkcs8PrivateKeyPem();
        if (string.Equals(invalid, "multiple", StringComparison.Ordinal)) pem += "\n" + key.ExportPkcs8PrivateKeyPem();
        await File.WriteAllTextAsync(fixture.Policy.AccountKeyPath, pem).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture.Policy.AccountKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var issuer = fixture.CreateIssuer();
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(pem, await File.ReadAllTextAsync(fixture.Policy.AccountKeyPath).ConfigureAwait(true)); Assert.Empty(fixture.Servers);
    }

    private sealed class IssuerFixture : IDisposable
    {
        private readonly RegistryStateDirectory _directory = new();
        public DevelopmentAcmeDnsProvider Dns { get; }
        public List<DevelopmentAcmeServer> Servers { get; } = [];
        public AcmeDns01IssuerPolicy Policy { get; }
        public IssuerFixture(DevelopmentAcmeDnsProvider? dns = null)
        {
            Dns = dns ?? new DevelopmentAcmeDnsProvider();
            Policy = new AcmeDns01IssuerPolicy { SiteDomain = "site.example", Directory = new Uri("https://ca.example/directory"),
                AccountKeyPath = Path.Combine(_directory.Path, "acme-account.pem"), ContactEmails = ["owner@example.org"], TermsAccepted = true, PollInterval = TimeSpan.FromMilliseconds(100) };
        }
        public CertesDns01CertificateIssuer CreateIssuer(string behavior = "normal", AcmeDns01IssuerPolicy? policy = null) => new(policy ?? Policy, Dns, () =>
        {
            var server = new DevelopmentAcmeServer(Dns, Policy.AccountKeyPath) { CorruptOrder = string.Equals(behavior, "order", StringComparison.Ordinal), CorruptAuthorization = string.Equals(behavior, "authorization", StringComparison.Ordinal), RejectAuthorization = string.Equals(behavior, "rejected", StringComparison.Ordinal), PendingOrder = string.Equals(behavior, "pending", StringComparison.Ordinal) };
            Servers.Add(server); return server;
        });
        public AcmeCertificateIssueRequest Request() => new("site", ["site.example", "*.site.example"], Policy.Directory.AbsoluteUri, Policy.ContactEmails, true);
        public void Dispose() { for (var i = 0; i < Servers.Count; i++) Servers[i].Dispose(); _directory.Dispose(); }
    }
}
