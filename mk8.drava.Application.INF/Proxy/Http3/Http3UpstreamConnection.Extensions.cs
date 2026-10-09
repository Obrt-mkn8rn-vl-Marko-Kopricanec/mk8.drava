using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;

namespace Mk8.Drava.Application.INF.Proxy.Http3;

internal sealed partial class Http3UpstreamConnection
{
    private int _ignoredFrames;
    private int _ignoredBytes;

    private async ValueTask<Http3FrameReadResult> ReadResponseFrameAsync(TimeSpan timeout,
        ProxyTimeoutKind timeoutKind, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(timeout, timeoutKind, cancellationToken).ConfigureAwait(false);
            if (frame.EndStream || !IsExtensionFrame(frame.Type)) return frame;
            // Resource bounds apply across every head/body/trailer phase of this stream.
            if (++_ignoredFrames > 128 || (_ignoredBytes = checked(_ignoredBytes + frame.Payload.Length)) > MaxFramePayloadBytes)
                throw new Http3UpstreamProtocolException("HTTP/3 ignored extension frames exceeded the stream resource bound.");
        }
    }

    private static bool IsExtensionFrame(long type) => type is not
        (0x0 or 0x1 or 0x2 or 0x3 or 0x4 or 0x5 or 0x6 or 0x7 or 0x8 or 0x9 or 0xd);
}
