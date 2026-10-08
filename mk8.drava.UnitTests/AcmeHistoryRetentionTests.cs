using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeHistoryRetentionTests
{
    [Fact]
    public async Task FailureKeepsPreviousSuccessAndNextSuccessKeepsPreviousFailureAsync()
    {
        var clock = new RegistryTimeProvider(); clock.AdjustUtc(DateTimeOffset.UtcNow - clock.GetUtcNow()); var now = clock.GetUtcNow();
        var active = new AcmeRenewalActiveCertificate(now.AddDays(-80), now.AddDays(10));
        using var fixture = new DevelopmentAsyncAcmeLifecycle(clock: clock) { Previous = active, LifetimeAwareRenewal = true, FailActivation = true, RetryAfter = TimeSpan.FromSeconds(30) };
        await fixture.Status.UpsertAsync(new AcmeCertificateLifecycleStatus("site", true, ["site.example"], true, "acme", active.NotBeforeUtc, active.NotAfterUtc,
            now.AddDays(-20), now.AddDays(-2), now.AddDays(-80), now.AddDays(-2), now.AddDays(-1), "failed", "Previous owned failure."), CancellationToken.None).ConfigureAwait(true);
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        var failed = fixture.Status.Get("site"); Assert.NotNull(failed); Assert.Equal("failed", failed.LastResult);
        Assert.Equal(now.AddDays(-80), failed.LastSucceededAtUtc); Assert.Equal(now, failed.LastFailedAtUtc);
        fixture.FailActivation = false; clock.Advance(TimeSpan.FromSeconds(31));
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        var succeeded = fixture.Status.Get("site"); Assert.NotNull(succeeded); Assert.Equal("succeeded", succeeded.LastResult);
        Assert.Equal(now, succeeded.LastFailedAtUtc); Assert.Equal(clock.GetUtcNow(), succeeded.LastSucceededAtUtc); Assert.Equal(2, fixture.Issued);
    }
}
