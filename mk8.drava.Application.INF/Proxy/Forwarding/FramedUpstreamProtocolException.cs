namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

internal sealed class FramedUpstreamProtocolException : IOException
{
    public FramedUpstreamProtocolException() { }
    public FramedUpstreamProtocolException(string? message) : base(message) { }
    public FramedUpstreamProtocolException(string? message, Exception? innerException) : base(message, innerException) { }
    public FramedUpstreamProtocolException(string? message, int hresult) : base(message, hresult) { }
}
