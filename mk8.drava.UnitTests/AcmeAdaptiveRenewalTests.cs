using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeAdaptiveRenewalTests
{
    [Theory]
    [InlineData(160)]
    [InlineData(1080)]
    [InlineData(2160)]
    public async Task FreshShortAndClassicCertificatesUseActualLifetimeWithoutImmediateReissuanceAsync(int lifetimeHours)
    {
        var before = DateTimeOffset.UtcNow.AddHours(-1); var after = before.AddHours(lifetimeHours);
        using var fixture = new DevelopmentAsyncAcmeLifecycle { Previous = new AcmeRenewalActiveCertificate(before, after), LifetimeAwareRenewal = true };
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        var status = fixture.Status.Get("site"); Assert.NotNull(status); Assert.Equal("not-due", status.LastResult); Assert.Equal(0, fixture.Issued);
        var lead = TimeSpan.FromTicks(Math.Min(TimeSpan.FromDays(30).Ticks, TimeSpan.FromHours(lifetimeHours).Ticks / 3));
        Assert.Equal(after.Subtract(lead), status.RenewalDueAtUtc);
    }

    [Fact]
    public async Task OwnerSecondsRetrySurvivesImmediateChecksWithoutRoundingToMinutesAsync()
    {
        using var fixture = new DevelopmentAsyncAcmeLifecycle { FailActivation = true, RetryAfter = TimeSpan.FromSeconds(30) };
        var before = DateTimeOffset.UtcNow;
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        var status = fixture.Status.Get("site"); Assert.NotNull(status); Assert.NotNull(status.NextAttemptNotBeforeUtc);
        Assert.InRange(status.NextAttemptNotBeforeUtc.Value, before.AddSeconds(30), DateTimeOffset.UtcNow.AddSeconds(30));
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true); Assert.Equal(1, fixture.Issued);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(3600)]
    public void OwnerSecondsCheckIntervalIsPreserved(int seconds) => Assert.Equal(TimeSpan.FromSeconds(seconds), new AcmeRenewalSchedulePolicy().ResolveDelay(
        AcmeRenewalScheduleInputReadResult.Available(new AcmeRenewalScheduleInput(true, 1, TimeSpan.FromSeconds(seconds)))));

    [Theory]
    [InlineData(9)]
    [InlineData(3601)]
    public void OwnerCheckIntervalOutsideBoundsIsRejected(int seconds) => Assert.Throws<InvalidDataException>(() => new AcmeRenewalSchedulePolicy().ResolveDelay(
        AcmeRenewalScheduleInputReadResult.Available(new AcmeRenewalScheduleInput(true, 1, TimeSpan.FromSeconds(seconds)))));
}
