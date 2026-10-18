using System.Runtime.ExceptionServices;

namespace Mk8.Drava.Application.INF.NodeRelay;

internal static class RelayConnectionCleanup
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "All cleanup failures are retained and rethrown after every owned resource has been attempted. A failure from one resource must not leak the resources that follow it.")]
    public static async ValueTask DisposeAsync(IAsyncDisposable? stream, params IDisposable?[] resources)
    {
        var failures = new List<Exception>();
        if (stream is not null)
        {
            try { await stream.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { failures.Add(exception); }
        }
        foreach (var resource in resources)
        {
            try { resource?.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        ThrowFailures(failures);
    }

    public static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
}
