using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class AcmeServingLifecycleTests
{
    [Fact]
    public async Task ImportedManagerPortsRetainTheFullIssuedChainAndPublishBeforeReportingActiveInputAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        var plansRole = await fixture.OpenRoleAsync(bootstrap).ConfigureAwait(true);
        await using var plansRoleLifetime = plansRole.ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(plansRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var lifecycle = new AcmeServingLifecycle(bootstrap, plans);
        var pendingInput = Assert.IsType<AcmeRenewalConfigurationInputReadResult.AvailableResult>(lifecycle.ReadInput()); Assert.Null(pendingInput.Input.Certificates[0].ActiveCertificate);
        var material = await lifecycle.WriteAndLoadAsync(new AcmeCertificateMaterialWriteRequest("owner-serving", "site", ["site.test", "*.site.test"], bootstrap.StateDirectory, fixture.Clock.GetUtcNow(), publicMaterial.Pfx), CancellationToken.None).ConfigureAwait(true);
        try
        {
            Assert.True(plans.Read("local").ServingPending);
            await lifecycle.ActivateAsync(material, CancellationToken.None).ConfigureAwait(true);
            Assert.False(plans.Read("local").ServingPending); Assert.Null(plans.ReadPublicationProof());
            var active = Assert.IsType<AcmeRenewalConfigurationInputReadResult.AvailableResult>(lifecycle.ReadInput()); Assert.NotNull(active.Input.Certificates[0].ActiveCertificate); Assert.True(active.Input.Certificates[0].LifetimeAwareRenewal);
            Assert.Equal(new DateTimeOffset(publicMaterial.Leaf.NotAfter.ToUniversalTime()), active.Input.Certificates[0].ActiveCertificate!.NotAfterUtc);
            Assert.Collection(plans.Read("local").Certificates, serving => Assert.NotEmpty(serving.Pfx), enrollment => Assert.NotEmpty(enrollment.Pfx));
        }
        finally { material.Certificate.Dispose(); }
    }

    [Fact]
    public async Task FailedActivationReleasesOnlyItsPendingMaterialCapabilityAndAllowsTheNextValidatedAttemptAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        using var foreign = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        bootstrap = bootstrap with { Controller = bootstrap.Controller! with { Acme = bootstrap.Controller.Acme with { PinnedServingRoot = new SecretBytes(foreign.Root.RawData) } } };
        var plansRole = await fixture.OpenRoleAsync(bootstrap).ConfigureAwait(true);
        await using var plansRoleLifetime = plansRole.ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(plansRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var lifecycle = new AcmeServingLifecycle(bootstrap, plans);
        var request = new AcmeCertificateMaterialWriteRequest("owner-serving", "site", ["site.test", "*.site.test"], bootstrap.StateDirectory, fixture.Clock.GetUtcNow(), publicMaterial.Pfx);
        var material = await lifecycle.WriteAndLoadAsync(request, CancellationToken.None).ConfigureAwait(true);
        try { await Assert.ThrowsAsync<InvalidDataException>(() => lifecycle.ActivateAsync(material, CancellationToken.None).AsTask()).ConfigureAwait(true); }
        finally { material.Certificate.Dispose(); }
        Assert.True(plans.Read("local").ServingPending); Assert.Null(plans.ReadPublicationProof());
        var next = await lifecycle.WriteAndLoadAsync(request, CancellationToken.None).ConfigureAwait(true);
        try
        {
            using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync().ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lifecycle.ActivateAsync(next, cancellation.Token).AsTask()).ConfigureAwait(true);
        }
        finally { next.Certificate.Dispose(); }
    }
}
