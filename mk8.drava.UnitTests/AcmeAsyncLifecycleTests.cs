using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class AcmeAsyncLifecycleTests
{
    [Fact]
    public async Task OwnedWritesAndActivationCompleteBeforeSuccessIsRecordedAsync()
    {
        using var fixture = new DevelopmentAsyncAcmeLifecycle { BlockWriter = true, BlockActivation = true };
        var operation = fixture.Manager.CheckRenewalsAsync(CancellationToken.None).AsTask();
        try
        {
            await fixture.WriterEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.False(operation.IsCompleted); Assert.Equal(0, fixture.Activated); Assert.Equal(0, fixture.Succeeded);
            fixture.ReleaseWriter(); await fixture.ActivationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.True(fixture.WriterExited.Task.IsCompletedSuccessfully); Assert.False(operation.IsCompleted); Assert.Equal(0, fixture.Succeeded);
            fixture.ReleaseActivation(); await operation.ConfigureAwait(true);
            Assert.Equal(1, fixture.Activated); Assert.Equal(1, fixture.Succeeded); Assert.Equal("succeeded", fixture.Status.Get("site")?.LastResult);
        }
        finally { fixture.ReleaseWriter(); fixture.ReleaseActivation(); await operation.ConfigureAwait(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationJoinsTheCurrentOperationWithoutPublishingOrRecordingSuccessAsync(bool activation)
    {
        using var fixture = new DevelopmentAsyncAcmeLifecycle { BlockWriter = !activation, BlockActivation = activation };
        using var cancellation = new CancellationTokenSource(); var operation = fixture.Manager.CheckRenewalsAsync(cancellation.Token).AsTask();
        await (activation ? fixture.ActivationEntered.Task : fixture.WriterEntered.Task).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        await cancellation.CancelAsync().ConfigureAwait(true);
        var canceled = false;
        try { await operation.ConfigureAwait(true); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled); Assert.Equal(0, fixture.Activated); Assert.Equal(0, fixture.Succeeded);
        Assert.True((activation ? fixture.ActivationExited.Task : fixture.WriterExited.Task).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task FailedActivationRetainsPriorActiveStatusAndBacksOffWithoutReissuingAsync()
    {
        var previous = new AcmeRenewalActiveCertificate(DateTimeOffset.UtcNow.AddDays(-70), DateTimeOffset.UtcNow.AddDays(20));
        using var fixture = new DevelopmentAsyncAcmeLifecycle { FailActivation = true, Previous = previous };
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true);
        var status = fixture.Status.Get("site"); Assert.NotNull(status); Assert.Equal("failed", status.LastResult); Assert.True(status.Active);
        Assert.Equal(previous.NotAfterUtc, status.NotAfterUtc); Assert.Equal(0, fixture.Activated); Assert.Equal(0, fixture.Succeeded); Assert.Equal(1, fixture.Failed);
        Assert.True(status.NextAttemptNotBeforeUtc > DateTimeOffset.UtcNow);
        await fixture.Manager.CheckRenewalsAsync(CancellationToken.None).ConfigureAwait(true); Assert.Equal(1, fixture.Issued);
    }
}
