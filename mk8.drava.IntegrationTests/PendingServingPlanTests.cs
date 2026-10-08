using Google.Protobuf;
using System.Security.Cryptography;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Configuration;
using Mk8.Drava.Gateway.Hosting;
using Mk8.Drava.Presentation.Certificates;
using Mk8.Drava.Transport.Certificates;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class PendingServingPlanTests
{
    [Fact]
    public async Task PublicFirstStartupCarriesOnlyPrivateTlsAndAcknowledgmentNeverPublishesReadinessAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PublicApplication(fixture, publicMaterial);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var pending = plans.Read("local"); Assert.True(pending.ServingPending); Assert.Empty(pending.Certificates[0].Pfx);
        Assert.True(plans.Acknowledge(Acknowledgment(pending))); Assert.Null(plans.ReadPublicationProof()); Assert.False(plans.IsAcknowledged);
        using var validated = new ValidatedServingPlan(pending, fixture.Gateway with { ServingTrust = bootstrap.Controller!.ServingTrust }, fixture.Clock, requireCurrent: true);
        Assert.False(validated.HasServingCertificate); Assert.Throws<InvalidOperationException>(() => validated.ServingContext);
        Assert.True(validated.EnrollmentCertificate.MatchesHostname("register.site.test", allowWildcards: false, allowCommonName: false));
        using var node = fixture.Authority.IssueNode("node", ["127.0.0.1"]); Assert.True(validated.ValidateClientCertificate(node)); Assert.False(validated.ValidateClientCertificate(publicMaterial.Client));
        using var gateway = new GatewayMaterialState();
        GatewayServingMaterial? material = new(pending, fixture.Gateway with { ServingTrust = bootstrap.Controller.ServingTrust });
        try { gateway.Install(material); material = null; }
        finally { material?.Dispose(); }
        using var lease = gateway.Acquire(); Assert.Null(lease.ServingContext); Assert.Null(lease.Certificate); Assert.NotNull(lease.EnrollmentContext);
        using var restored = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(pending.ToByteArray(), restored.Read("local").ToByteArray()); Assert.Null(restored.ReadPublicationProof());
    }

    [Fact]
    public async Task TrustedMaterialReplacesPendingAtANewGenerationAndNeedsANewAcknowledgmentAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow()); var bootstrap = PublicApplication(fixture, publicMaterial);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var pending = plans.Read("local"); plans.Acknowledge(Acknowledgment(pending));
        await PrivateCertificateFile.WriteNewAsync(bootstrap.Controller!.ServingCertificatePath, publicMaterial.Pfx, CancellationToken.None).ConfigureAwait(true);
        await plans.RenewIfRequiredAsync(CancellationToken.None).ConfigureAwait(true);
        var issued = plans.Read("local"); Assert.False(issued.ServingPending); Assert.Equal(pending.Generation + 1, issued.Generation); Assert.Null(plans.ReadPublicationProof());
        using var validated = new ValidatedServingPlan(issued, fixture.Gateway with { ServingTrust = bootstrap.Controller.ServingTrust }, fixture.Clock, requireCurrent: true);
        Assert.True(validated.HasServingCertificate); Assert.Equal(publicMaterial.Leaf.RawData, validated.ServingCertificate.RawData);
        Assert.True(plans.Acknowledge(Acknowledgment(issued))); Assert.NotNull(plans.ReadPublicationProof());
    }

    [Fact]
    public async Task InvalidCandidateAndMissingOwnerFileRetainAcceptedPublicMaterialAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow()); var bootstrap = PublicApplication(fixture, publicMaterial);
        await PrivateCertificateFile.WriteNewAsync(bootstrap.Controller!.ServingCertificatePath, publicMaterial.Pfx, CancellationToken.None).ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var accepted = plans.Read("local"); plans.Acknowledge(Acknowledgment(accepted));
        await File.WriteAllBytesAsync(bootstrap.Controller.ServingCertificatePath, new byte[256]).ConfigureAwait(true);
        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => plans.RenewIfRequiredAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(accepted.ToByteArray(), plans.Read("local").ToByteArray()); Assert.True(plans.IsAcknowledged);
        File.Delete(bootstrap.Controller.ServingCertificatePath); fixture.Clock.Advance(TimeSpan.FromDays(24));
        plans.Acknowledge(Acknowledgment(accepted)); await plans.RenewIfRequiredAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(accepted.ToByteArray(), plans.Read("local").ToByteArray()); Assert.True(plans.IsAcknowledged);
        using var restored = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(accepted.ToByteArray(), restored.Read("local").ToByteArray());
    }

    [Theory]
    [InlineData("private-mode")]
    [InlineData("missing-private")]
    [InlineData("business-material")]
    [InlineData("business-password")]
    [InlineData("business-expiry")]
    [InlineData("host")]
    [InlineData("private-scope")]
    [InlineData("digest")]
    public async Task MalformedPendingPlansAreRejectedBeforeTheyCanSupplyTlsAsync(string fault)
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow()); var bootstrap = PublicApplication(fixture, publicMaterial);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = plans.Read("local"); var trust = bootstrap.Controller!.ServingTrust;
        switch (fault)
        {
            case "private-mode": plan.ServingTrustMode = "site-ca"; plan.ServingRootFingerprint = ""; trust = new ServingTrustSettings(); break;
            case "missing-private": plan.Certificates.RemoveAt(1); break;
            case "business-material": plan.Certificates[0].Pfx = ByteString.CopyFrom(publicMaterial.Pfx); break;
            case "business-password": plan.Certificates[0].PfxPassword = "password"; break;
            case "business-expiry": plan.Certificates[0].NotAfterUnixSeconds = 1; break;
            case "host": plan.Certificates[0].HostNames[1] = "register.foreign.test"; break;
            case "private-scope": plan.Certificates[1].HostNames[1] = "admin.foreign.test"; break;
        }
        plan.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(plan));
        if (string.Equals(fault, "digest", StringComparison.Ordinal)) plan.ServingPending = false;
        Assert.Throws<InvalidDataException>(() => new ValidatedServingPlan(plan, fixture.Gateway with { ServingTrust = trust }, fixture.Clock, requireCurrent: true));
    }

    internal static ApplicationBootstrap PublicApplication(DevelopmentServingPlanFixture fixture, DevelopmentPublicServingCertificate material) => fixture.Application with
    {
        Controller = fixture.Application.Controller! with
        {
            ServingTrust = new ServingTrustSettings { Mode = "pinned", RootFingerprint = material.Root.GetCertHashString(HashAlgorithmName.SHA256) },
            ServingCertificatePath = Path.Combine(fixture.Application.StateDirectory, "public.pfx"),
            DnsPublication = new DnsPublicationSettings { Provider = "cloudflare", ZoneId = new string('a', 32), ZoneName = "site.test", CredentialPath = Path.Combine(fixture.Application.StateDirectory, "dns.token") },
            Acme = new AcmeIssuanceSettings { Enabled = true, TermsAccepted = true, DirectoryUrl = new Uri("https://development-ca.example/directory"), ContactEmails = ["ops@example.org"],
                AccountKeyPath = Path.Combine(fixture.Application.StateDirectory, "account.pem"), CleanupJournalPath = Path.Combine(fixture.Application.StateDirectory, "cleanup.json"), PinnedServingRootPath = Path.Combine(fixture.Application.StateDirectory, "public-root.der") },
        },
    };

    private static PlanAcknowledgment Acknowledgment(PresentationPlan plan) => new() { Version = 1, GatewayId = plan.GatewayId, Generation = plan.Generation, ContentSha256 = plan.ContentSha256, Applied = true };
}
