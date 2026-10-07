using Google.Protobuf;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Gateway.Hosting;
using Mk8.Drava.Presentation.Certificates;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class ServingPlanLifecycleTests
{
    [Fact]
    public async Task RestartPreservesMaterialAndGenerationButDoesNotRestoreAnAcknowledgmentAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        byte[] material;
        using (var plans = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true))
        {
            var plan = plans.Read("local");
            material = plan.ToByteArray();
            Assert.True(plans.Acknowledge(Acknowledgment(plan)));
            Assert.NotNull(plans.ReadPublicationProof());
            plan.Generation = 999;
            Assert.Equal(1UL, plans.Read("local").Generation);
        }
        using var restored = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(material, restored.Read("local").ToByteArray());
        Assert.False(restored.IsAcknowledged);
        Assert.Null(restored.ReadPublicationProof());
    }

    [Fact]
    public async Task HeartbeatExpiresMonotonicallyAndRenewalRequiresItsNewExactGenerationAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var prior = plans.Read("local");
        plans.Acknowledge(Acknowledgment(prior));
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        fixture.Clock.AdjustUtc(TimeSpan.FromMinutes(-1));
        Assert.False(plans.IsAcknowledged);
        fixture.Clock.Advance(TimeSpan.FromDays(24));
        await plans.RenewIfRequiredAsync(CancellationToken.None).ConfigureAwait(true);
        var renewed = plans.Read("local");
        Assert.Equal(2UL, renewed.Generation);
        Assert.NotEqual(prior.ContentSha256, renewed.ContentSha256);
        Assert.True(renewed.ValidUntilUnixSeconds > prior.ValidUntilUnixSeconds);
        Assert.False(plans.IsAcknowledged);
        Assert.Throws<InvalidDataException>(() => plans.Acknowledge(Acknowledgment(prior)));
        Assert.True(plans.Acknowledge(Acknowledgment(renewed)));
        using var restored = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(renewed.ToByteArray(), restored.Read("local").ToByteArray());
        Assert.False(restored.IsAcknowledged);
    }

    [Fact]
    public async Task ReplacedCertificateRemainsOwnedUntilItsConnectionLeaseEndsAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var prior = plans.Read("local");
        using var state = new GatewayMaterialState();
        InstallOwned(state, prior, fixture.Gateway);
        using var connection = state.Acquire();
        var selected = connection.Certificate!;
        var next = prior.Clone(); next.Generation = 2; next.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(next));
        InstallOwned(state, next, fixture.Gateway);
        Assert.NotEqual(IntPtr.Zero, selected.Handle);
        using var rollback = new GatewayServingMaterial(prior, fixture.Gateway);
        Assert.Throws<InvalidDataException>(() => state.Install(rollback));
        Assert.Equal(2UL, state.Read()!.Generation);
        connection.Dispose();
        Assert.Equal(IntPtr.Zero, selected.Handle);
    }

    private static PlanAcknowledgment Acknowledgment(PresentationPlan plan) => new()
        { Version = 1, GatewayId = plan.GatewayId, Generation = plan.Generation, ContentSha256 = plan.ContentSha256, Applied = true };

    private static void InstallOwned(GatewayMaterialState state, PresentationPlan plan, Mk8.Drava.Configuration.GatewayBootstrap bootstrap)
    {
        GatewayServingMaterial? candidate = new(plan, bootstrap);
        try { state.Install(candidate); candidate = null; }
        finally { candidate?.Dispose(); }
    }
}
