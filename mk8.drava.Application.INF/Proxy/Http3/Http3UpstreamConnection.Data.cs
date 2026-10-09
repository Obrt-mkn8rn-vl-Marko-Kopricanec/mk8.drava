using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Http3;

internal sealed partial class Http3UpstreamConnection
{
    private long _responseDataBytesRemaining;

    private async ValueTask<Http3FrameReadResult> ReadResponseDataChunkAsync(TimeSpan timeout,
        ProxyTimeoutKind timeoutKind, CancellationToken cancellationToken)
    {
        var length = (int)Math.Min(_responseDataBytesRemaining, _maxFramePayloadBytes);
        var payload = length == 0 ? [] : await ReadExactAsync(length, timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
        _responseDataBytesRemaining -= length;
        return new Http3FrameReadResult(false, Http3Codec.DataFrame, payload);
    }
}
