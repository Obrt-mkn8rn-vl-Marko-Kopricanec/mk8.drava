using Mk8.Drava.Application.BLL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PublicationFreshnessTests
{
    [Fact]
    public async Task DnsPublicationExpiresMonotonicallyDuringWallClockRollbackAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(fixture.Registry.State.Revision, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(2)) { Address = new PublishedServiceAddress("svc.site.example", "/") });
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        fixture.Clock.AdjustUtc(TimeSpan.FromMinutes(-1));
        Assert.True(fixture.Availability.Status(intent.Identity).LeaseValid);
        Assert.True(fixture.Availability.Status(intent.Identity).ReadinessValid);
        Assert.False(fixture.Availability.Status(intent.Identity).PublicationValid);
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
    }

    [Fact]
    public async Task AProbeForAnExpiredLeaseCannotCompleteAgainstItsRenewedEpochAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        var old = fixture.Availability.Status(intent.Identity).ReadinessGeneration;
        fixture.Clock.Advance(TimeSpan.FromSeconds(91));
        await fixture.Registry.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        var current = fixture.Availability.Status(intent.Identity).ReadinessGeneration;
        Assert.True(current > old);
        Assert.False(fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30), old));
        Assert.False(fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(1, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(20)), old));
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        Assert.True(fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30), current));
        Assert.Null(fixture.Availability.Status(intent.Identity).Publication);
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
    }

    [Fact]
    public void DeclaredReadinessUsesTwoSuccessesAndThreeFailuresWithoutCrossBootState()
    {
        var tracker = new ReadinessTracker();
        var identity = RegistryTestFixture.Intent().Identity;
        Assert.False(tracker.Record(identity, true));
        Assert.True(tracker.Record(identity, true));
        Assert.True(tracker.Record(identity, false));
        Assert.True(tracker.Record(identity, false));
        Assert.False(tracker.Record(identity, false));
        Assert.False(tracker.Record(identity, true));
        Assert.True(tracker.Record(identity, true));
        Assert.False(tracker.Record(RegistryTestFixture.Intent().Identity, true));
    }
}
