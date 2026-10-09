using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public sealed class Http1RequestHead
{
    public Http1RequestHead(string method, string target, string path, string version, string host, Http1RequestFraming framing, IEnumerable<ProxyHeaderField> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        Method = method;
        Target = target;
        Path = path;
        Version = version;
        Host = host;
        Framing = framing;
        Headers = ProxyHeaderFieldList.Copy(headers);
    }

    public string Method { get; }
    public string Target { get; }
    public string Path { get; }
    public string Version { get; }
    public string Host { get; }
    public Http1RequestFraming Framing { get; }
    public IReadOnlyList<ProxyHeaderField> Headers { get; }
    public long? ContentLength => Framing.ContentLength;
    public long? SourceContentLength { get; init; }
    public bool HasTransferEncoding => Framing.Kind == Http1BodyKind.Chunked;
}
