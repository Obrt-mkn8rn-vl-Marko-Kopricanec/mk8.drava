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
    [Theory]
    [InlineData(4.0)]
    [InlineData(9.5)]
    public async Task IssuerBoundRenewalRetainsTheExactPlanAndItsAppliedAcknowledgmentAsync(double elapsedDays)
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var root = fixture.Authority.PublicCertificate;
        fixture.Clock.Advance(new DateTimeOffset(root.NotAfter.ToUniversalTime()).Subtract(TimeSpan.FromDays(10)) - fixture.Clock.GetUtcNow());
        using var plans = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var initial = plans.Read("local");
        fixture.Clock.Advance(TimeSpan.FromDays(elapsedDays));
        Assert.True(plans.Acknowledge(Acknowledgment(initial)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => plans.RenewIfRequiredAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(initial.ToByteArray(), plans.Read("local").ToByteArray());
        Assert.True(plans.IsAcknowledged);
        using var restored = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(initial.ToByteArray(), restored.Read("local").ToByteArray());
        Assert.False(restored.IsAcknowledged);
    }

    [Fact]
    public async Task ConfiguredCertificateLifetimeAndAcknowledgmentExpirySurviveRestartAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        var bootstrap = fixture.Application with { Controller = fixture.Application.Controller! with { ServingPlan = DevelopmentLifecycleSettings.Fast.Serving } };
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var initial = plans.Read("local");
        Assert.Equal(6U, initial.AcknowledgmentLeaseSeconds);
        Assert.InRange(DateTimeOffset.FromUnixTimeSeconds(initial.ValidUntilUnixSeconds) - fixture.Clock.GetUtcNow(), TimeSpan.FromDays(7).Subtract(TimeSpan.FromSeconds(1)), TimeSpan.FromDays(7));
        Assert.True(plans.Acknowledge(Acknowledgment(initial)));
        fixture.Clock.Advance(TimeSpan.FromSeconds(7));
        fixture.Clock.AdjustUtc(TimeSpan.FromMinutes(-1));
        Assert.False(plans.IsAcknowledged);
        fixture.Clock.Advance(TimeSpan.FromDays(6));
        await plans.RenewIfRequiredAsync(CancellationToken.None).ConfigureAwait(true);
        var renewed = plans.Read("local");
        Assert.Equal(2UL, renewed.Generation);
        using var restored = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(renewed.ToByteArray(), restored.Read("local").ToByteArray());
        Assert.False(restored.IsAcknowledged);
        Assert.Equal(TimeSpan.FromSeconds(2), GatewayPlanService.RefreshDelay(DevelopmentLifecycleSettings.Fast.Gateway, renewed));
    }

    [Fact]
    public async Task PreviouslySavedPlanWithoutPolicyFieldsMigratesToANewGenerationAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var legacy = plans.Read("local");
        legacy.AcknowledgmentLeaseSeconds = 0; legacy.LeafLifetimeDays = 0;
        legacy.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(legacy));
        await Mk8.Drava.Application.DAL.Publication.GatewayMaterialStore.WriteAsync(fixture.Application.StateDirectory, legacy.ToByteArray(), CancellationToken.None).ConfigureAwait(true);
        using var restored = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var migrated = restored.Read("local");
        Assert.Equal(2UL, migrated.Generation);
        Assert.Equal(30U, migrated.AcknowledgmentLeaseSeconds);
        Assert.Equal(30U, migrated.LeafLifetimeDays);
        Assert.NotEqual(legacy.ContentSha256, migrated.ContentSha256);
        Assert.Throws<InvalidDataException>(() => restored.Acknowledge(Acknowledgment(legacy)));
    }

    [Fact]
    public async Task RetainedGenerationLimitRejectsOnlyReplacementThatWouldExceedLiveOwnershipAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var plans = await ServingPlanState.OpenAsync(fixture.Application, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var initial = plans.Read("local");
        using var state = new GatewayMaterialState(2);
        InstallOwned(state, initial, fixture.Gateway);
        using var first = state.Acquire();
        var second = initial.Clone(); second.Generation = 2; second.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(second));
        InstallOwned(state, second, fixture.Gateway);
        using var activeSecond = state.Acquire();
        var third = initial.Clone(); third.Generation = 3; third.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(third));
        Assert.Throws<InvalidDataException>(() => state.RequireInstallable(third));
        Assert.Equal(2UL, state.Read()!.Generation);
        activeSecond.Dispose();
        state.RequireInstallable(third);
        InstallOwned(state, third, fixture.Gateway);
        Assert.Equal(3UL, state.Read()!.Generation);
        Assert.NotEqual(IntPtr.Zero, first.Certificate!.Handle);
    }

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
