namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeListenerProjection
{
    public RuntimeListenerProjection(string Name, string Address, int Port, bool Enabled, RuntimeListenerTransport Transport, string? DefaultCertificateId, IReadOnlyList<RuntimeSniCertificateBindingProjection> SniCertificates, int Backlog, int MaxRequestHeadBytes, int MaxResponseHeadBytes, int MaxChunkLineBytes, int ForwardingBufferBytes, RuntimeListenerIdentityProjection Identity, RuntimeListenerProtocols Protocols, RuntimeHttp3Enablement Http3Enablement, RuntimeHttp3AltSvcProjection Http3AltSvc, RuntimeHttp2LimitsProjection Http2Limits, bool TcpTrafficEnabled, bool Http3ProtocolConfigured, RuntimeQuicListenerIdentityProjection? QuicIdentity, RuntimeHttp3ListenerReadinessProjection Http3)
    {
        ArgumentNullException.ThrowIfNull(Identity);
        ArgumentNullException.ThrowIfNull(Http3AltSvc);
        ArgumentNullException.ThrowIfNull(Http2Limits);
        ArgumentNullException.ThrowIfNull(Http3);
        RuntimeListenerFacts.ValidateIdentity(Name, Address, Port, Transport);
        RuntimeListenerFacts.Validate(Port, Backlog, MaxRequestHeadBytes, MaxResponseHeadBytes, MaxChunkLineBytes, ForwardingBufferBytes);
        RuntimeSniCertificateFacts.ValidateOptionalCertificateId(DefaultCertificateId);
        this.Name = Name;
        this.Address = Address;
        this.Port = Port;
        this.Enabled = Enabled;
        this.Transport = Transport;
        this.DefaultCertificateId = DefaultCertificateId;
        this.SniCertificates = RuntimeList.Copy(SniCertificates);
        this.Backlog = Backlog;
        this.MaxRequestHeadBytes = MaxRequestHeadBytes;
        this.MaxResponseHeadBytes = MaxResponseHeadBytes;
        this.MaxChunkLineBytes = MaxChunkLineBytes;
        this.ForwardingBufferBytes = ForwardingBufferBytes;
        this.Identity = Identity;
        this.Protocols = Protocols;
        this.Http3Enablement = Http3Enablement;
        this.Http3AltSvc = Http3AltSvc;
        this.Http2Limits = Http2Limits;
        this.TcpTrafficEnabled = TcpTrafficEnabled;
        this.Http3ProtocolConfigured = Http3ProtocolConfigured;
        this.QuicIdentity = QuicIdentity;
        this.Http3 = Http3;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public bool Enabled { get; }
    public RuntimeListenerTransport Transport { get; }
    public string? DefaultCertificateId { get; }
    public IReadOnlyList<RuntimeSniCertificateBindingProjection> SniCertificates { get; }
    public int Backlog { get; }
    public int MaxRequestHeadBytes { get; }
    public int MaxResponseHeadBytes { get; }
    public int MaxChunkLineBytes { get; }
    public int ForwardingBufferBytes { get; }
    public RuntimeListenerIdentityProjection Identity { get; }
    public RuntimeListenerProtocols Protocols { get; }
    public RuntimeHttp3Enablement Http3Enablement { get; }
    public RuntimeHttp3AltSvcProjection Http3AltSvc { get; }
    public RuntimeHttp2LimitsProjection Http2Limits { get; }
    public bool TcpTrafficEnabled { get; }
    public bool Http3ProtocolConfigured { get; }
    public RuntimeQuicListenerIdentityProjection? QuicIdentity { get; }
    public RuntimeHttp3ListenerReadinessProjection Http3 { get; }
}
