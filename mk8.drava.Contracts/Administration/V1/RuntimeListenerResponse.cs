namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeListenerResponse
{
    public RuntimeListenerResponse(string name, string address, int port, bool enabled, RuntimeListenerTransportResponse transport, string? defaultCertificateId, IReadOnlyList<RuntimeSniCertificateBindingResponse> sniCertificates, int backlog, int maxRequestHeadBytes, int maxResponseHeadBytes, int maxChunkLineBytes, int forwardingBufferBytes, RuntimeListenerIdentityResponse identity, RuntimeListenerProtocolsResponse protocols, RuntimeHttp3EnablementResponse http3Enablement, RuntimeHttp3AltSvcResponse http3AltSvc, RuntimeHttp2LimitsResponse http2Limits, bool tcpTrafficEnabled, bool http3ProtocolConfigured, RuntimeQuicListenerIdentityResponse? quicIdentity, RuntimeHttp3ListenerReadinessResponse http3)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(http3AltSvc);
        ArgumentNullException.ThrowIfNull(http2Limits);
        ArgumentNullException.ThrowIfNull(http3);
        Name = name;
        Address = address;
        Port = port;
        Enabled = enabled;
        Transport = transport;
        DefaultCertificateId = defaultCertificateId;
        SniCertificates = ApiResponseList.Copy(sniCertificates);
        Backlog = backlog;
        MaxRequestHeadBytes = maxRequestHeadBytes;
        MaxResponseHeadBytes = maxResponseHeadBytes;
        MaxChunkLineBytes = maxChunkLineBytes;
        ForwardingBufferBytes = forwardingBufferBytes;
        Identity = identity;
        Protocols = protocols;
        Http3Enablement = http3Enablement;
        Http3AltSvc = http3AltSvc;
        Http2Limits = http2Limits;
        TcpTrafficEnabled = tcpTrafficEnabled;
        Http3ProtocolConfigured = http3ProtocolConfigured;
        QuicIdentity = quicIdentity;
        Http3 = http3;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public bool Enabled { get; }
    public RuntimeListenerTransportResponse Transport { get; }
    public string? DefaultCertificateId { get; }
    public IReadOnlyList<RuntimeSniCertificateBindingResponse> SniCertificates { get; }
    public int Backlog { get; }
    public int MaxRequestHeadBytes { get; }
    public int MaxResponseHeadBytes { get; }
    public int MaxChunkLineBytes { get; }
    public int ForwardingBufferBytes { get; }
    public RuntimeListenerIdentityResponse Identity { get; }
    public RuntimeListenerProtocolsResponse Protocols { get; }
    public RuntimeHttp3EnablementResponse Http3Enablement { get; }
    public RuntimeHttp3AltSvcResponse Http3AltSvc { get; }
    public RuntimeHttp2LimitsResponse Http2Limits { get; }
    public bool TcpTrafficEnabled { get; }
    public bool Http3ProtocolConfigured { get; }
    public RuntimeQuicListenerIdentityResponse? QuicIdentity { get; }
    public RuntimeHttp3ListenerReadinessResponse Http3 { get; }
}
