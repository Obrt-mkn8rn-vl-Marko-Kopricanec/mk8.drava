using Mk8.Drava.Application.BLL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistryLifecycleTests
{
    [Fact]
    public async Task ReadinessAndPublicationAreBothRequiredAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        Assert.True(fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(120)));
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(fixture.Registry.State.Revision, 1, false, true, fixture.Clock.GetUtcNow().AddHours(1)));
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        fixture.Publish(intent);
        Assert.True(fixture.Availability.IsEligible(intent.Identity));
    }

    [Fact]
    public async Task HeartbeatsDoNotPersistOrPublishRevisionsAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        var revision = fixture.Registry.State.Revision;
        for (var index = 0; index < 30; index++)
            await fixture.Registry.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(revision, fixture.Registry.State.Revision);
        Assert.Equal(2, fixture.Repository.Commits);
    }

    [Fact]
    public async Task LeaseExpirySurvivesWallClockRollbackAndRequiresNewProofAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        fixture.Clock.Advance(TimeSpan.FromSeconds(91));
        fixture.Clock.AdjustUtc(TimeSpan.FromDays(-1));
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        await fixture.Registry.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        fixture.Publish(intent);
        Assert.True(fixture.Availability.IsEligible(intent.Identity));
    }

    [Fact]
    public async Task SupersededBootCannotReregisterOrRenewAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var original = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(original).ConfigureAwait(true);
        var replacement = RegistryTestFixture.Intent(instance: original.Identity.InstanceId);
        await fixture.ReadyAsync(replacement).ConfigureAwait(true);
        Assert.False(fixture.Availability.IsEligible(original.Identity));
        Assert.True(fixture.Availability.IsEligible(replacement.Identity));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, original, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Registry.RenewAsync(RegistryTestFixture.Fingerprint, original.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("192.0.2.11")]
    public async Task EnrollmentCannotChooseArbitraryEndpointsAsync(string address)
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, RegistryTestFixture.Intent(address: address), TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(fixture.Registry.State.Instances);
    }

    [Fact]
    public async Task DrainStopsAssignmentsAndCannotBeUndoneBySameBootAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        var upstream = await fixture.ReadyAsync(intent).ConfigureAwait(true);
        var route = RegistryTestFixture.Route([upstream]);
        using var existing = fixture.Selector.Reserve(route, null);
        Assert.NotNull(existing);
        await fixture.Registry.DrainAsync(RegistryTestFixture.Fingerprint, intent.Identity, CancellationToken.None).ConfigureAwait(true);
        Assert.Null(fixture.Selector.Reserve(route, null));
        Assert.Equal(1, fixture.Availability.ActiveRequests(upstream));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        existing.Dispose();
        existing.Dispose();
        Assert.Equal(0, fixture.Availability.ActiveRequests(upstream));
    }

    [Fact]
    public async Task StorageFailureClosesEligibilityAndPreventsFurtherRenewalsAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var intent = RegistryTestFixture.Intent();
        await fixture.ReadyAsync(intent).ConfigureAwait(true);
        fixture.Repository.FailNext = true;
        await Assert.ThrowsAsync<IOException>(async () => await fixture.Registry.DrainAsync(RegistryTestFixture.Fingerprint, intent.Identity, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Registry.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task OwnerAndContractPoolsCannotMergeAsync()
    {
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var other = new InstanceIntent(new RegisteredUpstreamIdentity("node", "owner", "svc", "v2", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")), "deployment", "127.0.0.1", 12345, "http1", "http", "/ready", "local", 1, false);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, other, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }
}
