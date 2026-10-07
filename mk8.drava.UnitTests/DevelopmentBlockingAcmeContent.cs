using System.Net;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentBlockingAcmeContent : HttpContent
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
        finally { Exited.TrySetResult(); }
    }

    protected override bool TryComputeLength(out long length) { length = 0; return false; }
}
