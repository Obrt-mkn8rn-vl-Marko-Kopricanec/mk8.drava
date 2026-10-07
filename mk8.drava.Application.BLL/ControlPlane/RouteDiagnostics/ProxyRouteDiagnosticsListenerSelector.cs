namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public static class ProxyRouteDiagnosticsListenerSelector
{
    public static IProxyRouteDiagnosticsListener? Select(IReadOnlyList<IProxyRouteDiagnosticsListener> listeners, string? listenerName, string scheme, int? port, string? protocol)
    {
        var transport = string.Equals(scheme, "https", StringComparison.Ordinal) ? "https" : "http";
        IEnumerable<IProxyRouteDiagnosticsListener> candidates = listeners.Where(static listener => listener.Enabled);
        if (!string.IsNullOrWhiteSpace(listenerName))
        {
            candidates = candidates.Where(listener => string.Equals(listener.Name, listenerName, StringComparison.OrdinalIgnoreCase));
        }

        candidates = candidates.Where(listener => string.Equals(listener.Transport, transport, StringComparison.OrdinalIgnoreCase));
        if (port.HasValue)
        {
            candidates = candidates.Where(listener => listener.Port == port.Value);
        }

        candidates = protocol switch
        {
            "http1" => candidates.Where(static listener => listener.SupportsHttp1),
            "http2" => candidates.Where(static listener => listener.SupportsHttp2),
            "http3" => candidates.Where(static listener => listener.Http3EnabledForTraffic),
            _ => candidates
        };
        return candidates.FirstOrDefault();
    }
}
