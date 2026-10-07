using System.Net;

namespace Mk8.Drava.IntegrationTests;

// Custom HttpContent permits HTTP/2 duplex; built-in byte/stream content waits
// for the entire upload before exposing the response to the caller.
internal sealed class DevelopmentDuplexContent(byte[] body, TimeSpan timeout) : HttpContent, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new(timeout);
    private readonly TaskCompletionSource _serialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _disposed;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Interlocked.Exchange(ref _started, 1);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        try { await stream.WriteAsync(body, linked.Token).ConfigureAwait(false); }
        finally { _serialized.TrySetResult(); }
    }

    protected override bool TryComputeLength(out long length) { length = body.Length; return true; }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _started) != 0) await _serialized.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally { Dispose(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0) _stop.Dispose();
        base.Dispose(disposing);
    }
}
