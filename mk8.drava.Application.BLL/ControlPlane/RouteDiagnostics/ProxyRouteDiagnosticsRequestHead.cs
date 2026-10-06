using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed class ProxyRouteDiagnosticsRequestHead
{
    public ProxyRouteDiagnosticsRequestHead(string method, string target, string path, string version, string host, ProxyRouteDiagnosticsRequestFraming framing, IEnumerable<ProxyHeaderField> headers)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(framing);
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
    public ProxyRouteDiagnosticsRequestFraming Framing { get; }
    public IReadOnlyList<ProxyHeaderField> Headers { get; }
}
