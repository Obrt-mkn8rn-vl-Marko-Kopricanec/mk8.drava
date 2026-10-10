using Microsoft.Extensions.Hosting;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.CompatibilityTests;

internal static partial class ClientHttp3Tests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "This regression observes repeated failing disposal. Its finally releases both test-owned signals and awaits disposal, catching only the exact controlled failure so cleanup cannot replace an assertion failure.")]
    public static async Task ScenarioCleanupJoinsStopFailureAndAsyncHostDisposalAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var directory = TemporaryDirectory.Create();
        var host = new DelayedScenarioHost();
        Http3ScenarioResult? result = null;
        try
        {
            result = new Http3ScenarioResult(directory, host, [], string.Empty, new ProxyMetrics().Snapshot(), string.Empty);
            await AssertScenarioCleanupAsync(result, host, directory.Path, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            host.StopCompletion.TrySetException(host.StopFailure);
            host.DisposeCompletion.TrySetResult();
            try
            {
                if (result is not null)
                {
                    await result.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (IOException exception) when (ReferenceEquals(exception, host.StopFailure))
            {
            }
        }
    }

    private static async Task AssertScenarioCleanupAsync(Http3ScenarioResult result, DelayedScenarioHost host, string directory, CancellationToken cancellationToken)
    {
        var first = result.DisposeAsync().AsTask();
        var second = result.DisposeAsync().AsTask();
        AssertEx.True(ReferenceEquals(first, second));
        await host.StopStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        AssertEx.Equal(1, host.StopCalls);
        AssertEx.True(Directory.Exists(directory));
        AssertEx.False(first.IsCompleted);
        host.StopCompletion.TrySetException(host.StopFailure);
        await host.DisposeStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        AssertEx.Equal(1, host.AsyncDisposeCalls);
        AssertEx.Equal(0, host.SyncDisposeCalls);
        AssertEx.True(Directory.Exists(directory));
        AssertEx.False(first.IsCompleted);
        AssertEx.False(second.IsCompleted);
        host.DisposeCompletion.TrySetResult();
        await AssertEx.ThrowsAsync<IOException>(async () => await result.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await AssertEx.ThrowsAsync<IOException>(async () => await result.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        AssertEx.False(Directory.Exists(directory));
        AssertEx.True(ReferenceEquals(first, result.DisposeAsync().AsTask()));
        AssertEx.Equal(1, host.StopCalls);
        AssertEx.Equal(1, host.AsyncDisposeCalls);
    }

    private sealed class DelayedScenarioHost : IHost, IAsyncDisposable
    {
        public TaskCompletionSource StopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisposeCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IOException StopFailure { get; } = new("Controlled scenario stop failure.");
        public int StopCalls { get; private set; }
        public int AsyncDisposeCalls { get; private set; }
        public int SyncDisposeCalls { get; private set; }
        public IServiceProvider Services { get; } = new EmptyScenarioServices();

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The test-owned completion signal uses RunContinuationsAsynchronously and has no external work or synchronization-context dependency.")]
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            StopStarted.TrySetResult();
            return StopCompletion.Task;
        }

        public void Dispose() => SyncDisposeCalls++;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "The test-owned completion signal uses RunContinuationsAsynchronously and has no external work or synchronization-context dependency.")]
        public async ValueTask DisposeAsync()
        {
            AsyncDisposeCalls++;
            DisposeStarted.TrySetResult();
            await DisposeCompletion.Task.ConfigureAwait(false);
        }
    }

    private sealed class EmptyScenarioServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
