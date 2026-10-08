namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ListenerOptions
{
    public string Name { get; init; } = "";
    public string Address { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8080;
    public bool Enabled { get; init; } = true;
    public string Transport { get; init; } = "http";
    public string Protocols { get; init; } = "http1";
    public string Http3Enablement { get; init; } = "";
    public bool Http3AltSvcEnabled { get; init; }
    public int Http3AltSvcMaxAgeSeconds { get; init; } = 86400;
    public string? DefaultCertificateId { get; init; }
    public IList<SniCertificateOptions> SniCertificates { get; init; } = [];
    public int Backlog { get; init; } = 512;
    public int MaxRequestHeadBytes { get; init; } = 32 * 1024;
    public int MaxResponseHeadBytes { get; init; } = 32 * 1024;
    public int MaxChunkLineBytes { get; init; } = 1024;
    public int ForwardingBufferBytes { get; init; } = 64 * 1024;
    public int Http2MaxConcurrentStreams { get; init; } = 100;
    public int Http2MaxHeaderListBytes { get; init; } = 32 * 1024;
    public int Http2MaxFrameSize { get; init; } = 16 * 1024;
}
