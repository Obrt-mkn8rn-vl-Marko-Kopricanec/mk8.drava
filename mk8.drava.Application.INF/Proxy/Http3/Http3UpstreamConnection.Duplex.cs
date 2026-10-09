using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Http3;

internal sealed partial class Http3UpstreamConnection
{
    private readonly TaskCompletionSource _requestCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _headFramesObserved;

    private ValueTask<Http3FrameReadResult> ReadHeadFrameAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        !_requestCompleted.Task.IsCompleted && _headFramesObserved == 0
            ? ReadDuringUploadAsync(timeout, cancellationToken)
            : ReadResponseFrameAsync(timeout, ProxyTimeoutKind.UpstreamResponseHead, cancellationToken);

    private async ValueTask<Http3FrameReadResult> ReadDuringUploadAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var read = ReadResponseFrameAsync(Timeout.InfiniteTimeSpan, ProxyTimeoutKind.UpstreamResponseHead, deadline.Token).AsTask();
        try
        {
            if (await Task.WhenAny(read, _requestCompleted.Task).WaitAsync(deadline.Token).ConfigureAwait(false) != read)
                deadline.CancelAfter(timeout);
            return await read.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProxyTimeoutException(ProxyTimeoutKind.UpstreamResponseHead, timeout);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            try { await read.ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException) { }
        }
    }
}
