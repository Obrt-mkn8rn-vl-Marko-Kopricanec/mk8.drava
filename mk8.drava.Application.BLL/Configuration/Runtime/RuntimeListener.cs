namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeListener
{
    public RuntimeListener(string Name, string Address, int Port, bool Enabled, RuntimeListenerTransport Transport, string? DefaultCertificateId, IReadOnlyList<RuntimeSniCertificateBinding> SniCertificates, int Backlog, int MaxRequestHeadBytes, int MaxResponseHeadBytes, int MaxChunkLineBytes, int ForwardingBufferBytes) : this(Name, Address, Port, Enabled, Transport, DefaultCertificateId, SniCertificates, Backlog, MaxRequestHeadBytes, MaxResponseHeadBytes, MaxChunkLineBytes, ForwardingBufferBytes, RuntimeListenerProtocols.Http1, RuntimeHttp3Enablement.Default, RuntimeHttp3AltSvcOptions.Disabled, RuntimeHttp2Limits.Default)
    {
    }

    public RuntimeListener(string Name, string Address, int Port, bool Enabled, RuntimeListenerTransport Transport, string? DefaultCertificateId, IReadOnlyList<RuntimeSniCertificateBinding> SniCertificates, int Backlog, int MaxRequestHeadBytes, int MaxResponseHeadBytes, int MaxChunkLineBytes, int ForwardingBufferBytes, RuntimeListenerProtocols Protocols, RuntimeHttp3Enablement Http3Enablement, RuntimeHttp3AltSvcOptions Http3AltSvc, RuntimeHttp2Limits Http2Limits)
    {
        RuntimeListenerFacts.ValidateIdentity(Name, Address, Port, Transport);
        RuntimeListenerFacts.Validate(Port, Backlog, MaxRequestHeadBytes, MaxResponseHeadBytes, MaxChunkLineBytes, ForwardingBufferBytes);
        RuntimeSniCertificateFacts.ValidateOptionalCertificateId(DefaultCertificateId);
        ArgumentNullException.ThrowIfNull(Http3AltSvc);
        ArgumentNullException.ThrowIfNull(Http2Limits);
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
        this.Protocols = Protocols;
        this.Http3Enablement = Http3Enablement;
        this.Http3AltSvc = Http3AltSvc;
        this.Http2Limits = Http2Limits;
    }

    public string Name { get; }
    public string Address { get; }
    public int Port { get; }
    public bool Enabled { get; }
    public RuntimeListenerTransport Transport { get; }
    public string? DefaultCertificateId { get; }
    public IReadOnlyList<RuntimeSniCertificateBinding> SniCertificates { get; }
    public int Backlog { get; }
    public int MaxRequestHeadBytes { get; }
    public int MaxResponseHeadBytes { get; }
    public int MaxChunkLineBytes { get; }
    public int ForwardingBufferBytes { get; }
    public RuntimeListenerIdentity Identity => RuntimeListenerIdentity.From(this);
    public RuntimeListenerProtocols Protocols { get; }
    public RuntimeHttp3Enablement Http3Enablement { get; }
    public RuntimeHttp3AltSvcOptions Http3AltSvc { get; }
    public RuntimeHttp2Limits Http2Limits { get; }
    public bool TcpTrafficEnabled => Protocols.HasTcpProtocols();
    public bool Http3ProtocolConfigured => Protocols.HasHttp3();
    public RuntimeQuicListenerIdentity? QuicIdentity => Http3.EnabledForTraffic ? RuntimeQuicListenerIdentity.From(this) : null;
    public RuntimeHttp3ListenerReadiness Http3 => RuntimeHttp3ListenerReadiness.From(this);

    public RuntimeListener WithSniCertificates(IReadOnlyList<RuntimeSniCertificateBinding> sniCertificates)
    {
        return new RuntimeListener(Name, Address, Port, Enabled, Transport, DefaultCertificateId, sniCertificates, Backlog, MaxRequestHeadBytes, MaxResponseHeadBytes, MaxChunkLineBytes, ForwardingBufferBytes, Protocols, Http3Enablement, Http3AltSvc, Http2Limits);
    }
}
