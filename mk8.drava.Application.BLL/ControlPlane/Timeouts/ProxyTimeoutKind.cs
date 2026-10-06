namespace Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
public enum ProxyTimeoutKind
{
    ClientRequestHead,
    ClientKeepAliveIdle,
    ClientRequestBodyIdle,
    UpstreamConnect,
    UpstreamResponseHead,
    UpstreamResponseBodyIdle,
    DownstreamWrite,
    TlsHandshake,
    TunnelIdle
}
