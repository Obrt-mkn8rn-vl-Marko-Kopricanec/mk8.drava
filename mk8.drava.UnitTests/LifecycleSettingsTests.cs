using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class LifecycleSettingsTests
{
    [Theory]
    [InlineData(300, 60, 15, 5)]
    [InlineData(90, 30, 90, 30)]
    [InlineData(15, 4, 300, 4)]
    public void SdkRenewsWithinBothSiteAndAgentLifetimes(int lease, int renew, int mappingLease, int expectedSeconds)
    {
        var settings = new Mk8.Drava.Registration.DravaRegistrationOptions { RenewJitterPercent = 0 };
        var status = new Mk8.Drava.Contracts.Registration.V1.RegistrationStatus
            { LeaseSeconds = lease, RenewAfterSeconds = renew, Phase = Mk8.Drava.Contracts.Registration.V1.RegistrationPhase.Ready };
        var agent = new NodeAgentDescriptor { MappingLeaseSeconds = mappingLease };
        var delay = Mk8.Drava.Registration.DravaRegistrationHostedService.RenewalDelay(settings, status, agent);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
        Assert.True(delay < TimeSpan.FromSeconds(Math.Min(lease, mappingLease)));
    }

    [Theory]
    [InlineData(15, 5)]
    [InlineData(300, 60)]
    public async Task SignedSitePolicyControlsTheActualMonotonicLeaseAsync(int leaseSeconds, int renewSeconds)
    {
        using var fixture = new EnrollmentTestFixture(policy: new RegistrationRuntimePolicy { LeaseSeconds = leaseSeconds, RenewAfterSeconds = renewSeconds });
        await fixture.InitializeAsync().ConfigureAwait(true);
        var command = EnrollmentTestFixture.Command();
        var status = await fixture.Handler.SubmitAsync(fixture.Sign(command), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(leaseSeconds, status.LeaseSeconds);
        Assert.Equal(renewSeconds, status.RenewAfterSeconds);
        var identity = fixture.Registry.State.Instances[command.Identity.InstanceId].Identity;
        fixture.Clock.Advance(TimeSpan.FromSeconds(leaseSeconds - 1));
        Assert.True(fixture.Availability.Status(identity).LeaseValid);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Clock.AdjustUtc(TimeSpan.FromMinutes(-10));
        Assert.False(fixture.Availability.Status(identity).LeaseValid);
    }

    [Fact]
    public void ConfiguredHysteresisAndNodeMappingLifetimeChangeEligibility()
    {
        var identity = RegistryTestFixture.Intent().Identity;
        var readiness = new ReadinessTracker(1, 2);
        Assert.True(readiness.Record(identity, true));
        Assert.True(readiness.Record(identity, false));
        Assert.False(readiness.Record(identity, false));
        Assert.True(readiness.Record(identity, true));
        using var fixture = new RelayTestFixture(TimeSpan.FromSeconds(15));
        fixture.Clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(fixture.Command.Identity.InstanceId, fixture.Authorize(fixture.Capability()).Endpoint.Identity.InstanceId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability()));
    }

    [Theory]
    [InlineData(14, 4, 30)]
    [InlineData(301, 30, 30)]
    [InlineData(15, 6, 30)]
    [InlineData(90, 30, 11)]
    public void InvalidLeaseOrProofCadenceCannotReachRuntime(int leaseSeconds, int renewSeconds, int proofSeconds)
    {
        var settings = new RegistrationSettings { LeaseSeconds = leaseSeconds, RenewAfterSeconds = renewSeconds, ReadinessValiditySeconds = proofSeconds };
        var policy = new RegistrationRuntimePolicy { LeaseSeconds = leaseSeconds, RenewAfterSeconds = renewSeconds, ReadinessValiditySeconds = proofSeconds };
        Assert.Throws<InvalidDataException>(settings.Validate);
        Assert.Throws<InvalidDataException>(policy.Validate);
    }

    [Theory]
    [InlineData(1, 1, 30)]
    [InlineData(30, 30, 30)]
    [InlineData(30, 7, 4)]
    [InlineData(30, 7, 301)]
    public void InvalidServingLifetimesAreRejected(int lifetime, int lead, int acknowledgment)
    {
        var settings = new ServingPlanSettings { LeafLifetimeDays = lifetime, RenewalLeadDays = lead, AcknowledgmentLeaseSeconds = acknowledgment };
        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Theory]
    [InlineData("{\"plan\":{\"refreshSeconds\":2,\"refreshSeconds\":60}}")]
    [InlineData("{\"plan\":{\"unknownDeadline\":2}}")]
    [InlineData("{\"plan\":null}")]
    public async Task AmbiguousUnknownOrNullBootstrapSettingsAreRejectedAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "drava_settings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json).ConfigureAwait(true);
            var exception = await Record.ExceptionAsync(() => BootstrapFile.LoadAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
            Assert.True(exception is InvalidDataException or System.Text.Json.JsonException);
        }
        finally { File.Delete(path); }
    }
}

