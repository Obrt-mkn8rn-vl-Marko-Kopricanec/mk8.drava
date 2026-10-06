using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record RouteMatchDryRunRequest
{
    public RouteMatchDryRunRequest(string Scheme, string Host, int? Port, string Method, string Path, string Query, IReadOnlyDictionary<string, string?> Headers, string? ClientIp, string? ListenerName, string? Protocol = null)
    {
        ArgumentNullException.ThrowIfNull(Scheme);
        ArgumentNullException.ThrowIfNull(Host);
        ArgumentNullException.ThrowIfNull(Method);
        ArgumentNullException.ThrowIfNull(Path);
        ArgumentNullException.ThrowIfNull(Query);
        ArgumentNullException.ThrowIfNull(Headers);
        this.Scheme = Scheme;
        this.Host = Host;
        this.Port = Port;
        this.Method = Method;
        this.Path = Path;
        this.Query = Query;
        this.Headers = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(Headers));
        this.ClientIp = ClientIp;
        this.ListenerName = ListenerName;
        this.Protocol = Protocol;
    }

    public string Scheme { get; }
    public string Host { get; }
    public int? Port { get; }
    public string Method { get; }
    public string Path { get; }
    public string Query { get; }
    public IReadOnlyDictionary<string, string?> Headers { get; }
    public string? ClientIp { get; }
    public string? ListenerName { get; }
    public string? Protocol { get; }
}
