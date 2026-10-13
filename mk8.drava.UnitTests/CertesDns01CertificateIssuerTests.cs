using System.Security.Cryptography;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class CertesDns01CertificateIssuerTests
{
    [Fact]
    public async Task FactoryCancellationClosesARealSocketsHandlerBeforeIssuanceReturnsAsync()
    {
        using var fixture = new IssuerFixture();
        using var cancellation = new CancellationTokenSource();
        using var handler = new SocketsHttpHandler();
        using var issuer = new CertesDns01CertificateIssuer(fixture.Policy, fixture.Dns, () =>
        {
            cancellation.Cancel();
            return handler;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), cancellation.Token).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Throws<ObjectDisposedException>(() => handler.AllowAutoRedirect = false);
        Assert.Equal(0, fixture.Dns.Published);
        Assert.Empty(fixture.Dns.Records);
    }

    [Fact]
    public async Task CancellationInsideHandlerFactoryDisposesCreatedHandlerBeforeReturningAsync()
    {
        using var fixture = new IssuerFixture();
        using var cancellation = new CancellationTokenSource();
        using var handler = new DevelopmentAcmeHttpHandler();
        using var issuer = new CertesDns01CertificateIssuer(fixture.Policy, fixture.Dns, () =>
        {
            cancellation.Cancel();
            return handler;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), cancellation.Token).ConfigureAwait(false)).ConfigureAwait(true);
        Assert.Equal(1, handler.Disposals);
        Assert.Equal(0, handler.Requests);
        Assert.Equal(0, fixture.Dns.Published);
        Assert.Empty(fixture.Dns.Records);
    }

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
            Assert.True(DevelopmentCardinality.RequireSingle(privateLeaves).HasPrivateKey);
            var leaf = privateLeaves[0];
            Assert.Equal(2, certificates.Count);
            Assert.NotEmpty(DevelopmentCardinality.RequireSingle(fixture.Servers).RootCertificate);
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
        await PortableFixtureFile.WriteAsync(fixture.CollectionPath, PortableConfiguration.Serialize(new AcmeIssuanceSettings { AccountKey = fixture.Policy.AccountKey }), CancellationToken.None).ConfigureAwait(true);
        var first = await PortableConfiguration.LoadDocumentAsync<AcmeIssuanceSettings>(fixture.CollectionPath, CancellationToken.None).ConfigureAwait(true);
        using (var issuer = fixture.CreateIssuer(policy: fixture.Policy with { AccountKey = first.AccountKey }))
            Assert.IsType<AcmeCertificateIssueResult.IssuedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        var reopened = await PortableConfiguration.LoadDocumentAsync<AcmeIssuanceSettings>(fixture.CollectionPath, CancellationToken.None).ConfigureAwait(true);
        using (var issuer = fixture.CreateIssuer(policy: fixture.Policy with { AccountKey = reopened.AccountKey }))
            Assert.IsType<AcmeCertificateIssueResult.IssuedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(first.AccountKey, reopened.AccountKey);
        Assert.Equal(2, fixture.Servers.Count);
        Assert.Equal(fixture.Servers[0].AccountPublicKey, fixture.Servers[1].AccountPublicKey);
        Assert.False(File.Exists(fixture.UnrelatedAccountPath));
        Assert.DoesNotContain(first.AccountKey.Reveal(), await File.ReadAllTextAsync(fixture.CollectionPath, CancellationToken.None).ConfigureAwait(true), StringComparison.Ordinal);
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
        Assert.Empty(fixture.Servers); Assert.False(File.Exists(fixture.UnrelatedAccountPath)); Assert.Empty(fixture.Dns.Records);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingPropagationTimesOutWithoutNotifyingTheCaAndJoinsCleanupAsync(bool blockedProof)
    {
        var clock = new DevelopmentOperationDeadlineClock();
        using var fixture = new IssuerFixture(new DevelopmentAcmeDnsProvider { MissingProof = true, BlockProof = blockedProof });
        using var issuer = fixture.CreateIssuer(policy: fixture.Policy with { OperationTimeout = TimeSpan.FromSeconds(1) }, operationTimeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var issue = issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), cancellation.Token).AsTask();
        try
        {
            await fixture.Dns.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, cancellation.Token).ConfigureAwait(true);
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.False(issue.IsCompleted);
            Assert.Equal(0, fixture.Dns.Removed);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issue.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, cancellation.Token).ConfigureAwait(true));
        }
        finally { await cancellation.CancelAsync().ConfigureAwait(true); await issue.ConfigureAwait(true); }
        Assert.Equal(1, fixture.Dns.Published); Assert.Equal(1, fixture.Dns.Removed); Assert.Empty(fixture.Dns.Records);
        Assert.True(fixture.Dns.Exited.Task.IsCompletedSuccessfully);
        Assert.Equal(0, fixture.Servers[0].Validations); Assert.Equal(0, fixture.Servers[0].Finalizations);
    }

    [Fact]
    public async Task OperationExpiryBeforePublicationDisposesTheHandlerAndLeavesNoChallengeAsync()
    {
        var clock = new DevelopmentOperationDeadlineClock();
        using var fixture = new IssuerFixture();
        using var handler = new DevelopmentAcmeHttpHandler();
        using var issuer = new CertesDns01CertificateIssuer(fixture.Policy with { OperationTimeout = TimeSpan.FromSeconds(1) }, fixture.Dns, () =>
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            return handler;
        }, clock);
        Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(1, handler.Disposals);
        Assert.Equal(0, handler.Requests);
        Assert.Equal(0, fixture.Dns.Published);
        Assert.Equal(0, fixture.Dns.Removed);
        Assert.Empty(fixture.Dns.Records);
    }

    [Fact]
    public async Task OwnerCancellationJoinsBlockedDnsProofAndCleanupAndConcurrentOrderIsRejectedAsync()
    {
        using var fixture = new IssuerFixture(new DevelopmentAcmeDnsProvider { BlockProof = true });
        using var issuer = fixture.CreateIssuer();
        using var cancellation = new CancellationTokenSource();
        var issue = Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), cancellation.Token).ConfigureAwait(false));
        try
        {
            await fixture.Dns.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token).ConfigureAwait(true);
            Assert.IsType<AcmeCertificateIssueResult.FailedResult>(await issuer.IssueAsync(fixture.Request(), new AcmeChallengeStore(), CancellationToken.None).ConfigureAwait(true));
            Assert.True(DevelopmentCardinality.RequireSingle(fixture.Servers).Requests > 0);
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
        Assert.Equal(0, fixture.Servers[0].Finalizations); Assert.Equal("_acme-challenge.site.example", DevelopmentCardinality.RequireSingle(fixture.Dns.Records).Host);
    }

    [Fact]
    public void InvalidConfiguredAccountMaterialIsRetainedAndNoNetworkOperationRuns()
    {
        using var fixture = new IssuerFixture();
        var material = new SecretText(new string('\0', 256));
        var invalid = fixture.Policy with { AccountKey = material };
        Assert.Throws<InvalidDataException>(() => fixture.CreateIssuer(policy: invalid));
        Assert.Same(material, invalid.AccountKey);
        Assert.Empty(fixture.Servers); Assert.Empty(fixture.Dns.Records);
        Assert.False(File.Exists(fixture.UnrelatedAccountPath));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("different-curve")]
    [InlineData("multiple")]
    [InlineData("absent")]
    public void ConfiguredAccountRequiresOnePrivateP256KeyAndIsNeverRegenerated(string invalid)
    {
        using var fixture = new IssuerFixture();
        using var key = ECDsa.Create(string.Equals(invalid, "different-curve", StringComparison.Ordinal) ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256);
        var pem = string.Equals(invalid, "public", StringComparison.Ordinal) ? key.ExportSubjectPublicKeyInfoPem() : key.ExportPkcs8PrivateKeyPem();
        if (string.Equals(invalid, "multiple", StringComparison.Ordinal)) pem += "\n" + key.ExportPkcs8PrivateKeyPem();
        if (string.Equals(invalid, "absent", StringComparison.Ordinal)) pem = "";
        var policy = fixture.Policy with { AccountKey = new SecretText(pem) };
        Assert.Throws<InvalidDataException>(() => fixture.CreateIssuer(policy: policy));
        Assert.Equal(pem, policy.AccountKey.Reveal()); Assert.Empty(fixture.Servers); Assert.Empty(fixture.Dns.Records);
        Assert.False(File.Exists(fixture.UnrelatedAccountPath));
    }

    [Fact]
    public async Task LegacyAccountFileCannotReplaceAMissingProtectedValueAsync()
    {
        using var fixture = new IssuerFixture();
        var legacy = fixture.Policy.AccountKey.Reveal();
        await File.WriteAllTextAsync(fixture.UnrelatedAccountPath, legacy, CancellationToken.None).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture.UnrelatedAccountPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Throws<InvalidDataException>(() => fixture.CreateIssuer(policy: fixture.Policy with { AccountKey = SecretText.Empty }));
        Assert.Equal(legacy, await File.ReadAllTextAsync(fixture.UnrelatedAccountPath, CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(fixture.Servers); Assert.Empty(fixture.Dns.Records);
    }

    [Fact]
    public void LegacyAccountLocatorIsRejected()
    {
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<AcmeIssuanceSettings>("{\"accountKeyPath\":\"/reserved/fixture\"}"u8.ToArray()));
    }

    private sealed class IssuerFixture : IDisposable
    {
        private readonly RegistryStateDirectory _directory = new();
        public string CollectionPath => Path.Combine(_directory.Path, ".env");
        public string UnrelatedAccountPath => Path.Combine(_directory.Path, "account.pem");
        public DevelopmentAcmeDnsProvider Dns { get; }
        public List<DevelopmentAcmeServer> Servers { get; } = [];
        public AcmeDns01IssuerPolicy Policy { get; }
        public IssuerFixture(DevelopmentAcmeDnsProvider? dns = null)
        {
            Dns = dns ?? new DevelopmentAcmeDnsProvider();
            Policy = new AcmeDns01IssuerPolicy { SiteDomain = "site.example", Directory = new Uri("https://ca.example/directory"),
                AccountKey = DevelopmentAcmeServer.CreateAccountKey(), ContactEmails = ["owner@example.org"], TermsAccepted = true, PollInterval = TimeSpan.FromMilliseconds(100), };
        }
        public CertesDns01CertificateIssuer CreateIssuer(string behavior = "normal", AcmeDns01IssuerPolicy? policy = null, TimeProvider? operationTimeProvider = null) => new(policy ?? Policy, Dns, () =>
        {
            var server = new DevelopmentAcmeServer(Dns, (policy ?? Policy).AccountKey) { CorruptOrder = string.Equals(behavior, "order", StringComparison.Ordinal), CorruptAuthorization = string.Equals(behavior, "authorization", StringComparison.Ordinal), RejectAuthorization = string.Equals(behavior, "rejected", StringComparison.Ordinal), PendingOrder = string.Equals(behavior, "pending", StringComparison.Ordinal) };
            Servers.Add(server); return server;
        }, operationTimeProvider);
        public AcmeCertificateIssueRequest Request() => new("site", ["site.example", "*.site.example"], Policy.Directory.AbsoluteUri, Policy.ContactEmails, TermsAccepted: true);
        public void Dispose() { for (var i = 0; i < Servers.Count; i++) Servers[i].Dispose(); _directory.Dispose(); }
    }
}
