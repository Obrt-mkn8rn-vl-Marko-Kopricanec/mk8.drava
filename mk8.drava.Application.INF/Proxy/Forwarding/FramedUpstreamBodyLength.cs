using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

internal sealed class FramedUpstreamBodyLength
{
    private readonly long? _expected;
    private long _received;
    private bool _completed;

    public FramedUpstreamBodyLength(Http1ResponseFraming framing)
    {
        ArgumentNullException.ThrowIfNull(framing);
        _expected = framing.Kind == Http1BodyKind.None ? 0 : framing.ContentLength;
    }

    public void Observe(int dataBytes, bool endStream)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dataBytes);
        if (_completed) throw new FramedUpstreamProtocolException("Upstream sent content after stream completion.");
        if (dataBytes > long.MaxValue - _received || (_expected is { } expected && dataBytes > expected - _received))
            throw new FramedUpstreamProtocolException("Upstream content exceeded its declared length.");
        _received += dataBytes;
        if (!endStream) return;
        if (_expected is { } length && _received != length)
            throw new FramedUpstreamProtocolException("Upstream ended before its declared content length.");
        _completed = true;
    }
}
