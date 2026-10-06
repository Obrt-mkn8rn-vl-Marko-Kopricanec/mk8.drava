namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyListenerStatusResponse(string Name, string Identity, string BindKey, string Kind, string Address, int Port, string Transport, bool TlsEnabled, string Protocols, ProxyListenerHttp3StatusResponse Http3, int Http2MaxConcurrentStreams, int Http2MaxHeaderListBytes, int Http2MaxFrameSize, ProxyListenerStateResponse State, long ActiveConnections, DateTimeOffset? StartedAtUtc, DateTimeOffset? StoppedAtUtc, string? LastError)
{
}
