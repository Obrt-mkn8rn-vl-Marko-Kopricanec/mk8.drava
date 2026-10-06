namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public sealed class ProxyRequestDiagnosticsReader : IProxyRequestDiagnosticsReader
{
    private readonly IProxyRequestDiagnosticsSource _source;
    public ProxyRequestDiagnosticsReader(IProxyRequestDiagnosticsSource source)
    {
        _source = source;
    }

    public IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Recent(int limit)
    {
        return _source.Recent(limit);
    }
}
