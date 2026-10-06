namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public static class ConfigLintListenerAnalyzer
{
    public static IReadOnlyList<ConfigLintFinding> Analyze(ProxyConfigLintConfigurationSnapshot snapshot, bool activeRuntime, IReadOnlyList<ProxyConfigLintRuntimeListenerState> runtimeListeners, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<ConfigLintFinding> findings = [];
        AddBindFindings(snapshot, sourceName, findings);
        AddHttp3Findings(snapshot, activeRuntime, runtimeListeners, sourceName, findings);
        AddHttpsRedirectFindings(snapshot, sourceName, findings);
        return findings;
    }

    private static void AddBindFindings(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName, List<ConfigLintFinding> findings)
    {
        foreach (var group in snapshot.Listeners.Where(static listener => listener.Enabled).GroupBy(static listener => $"{listener.Address}|{listener.Port}|{listener.Transport}", StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
        {
            findings.Add(ConfigLintFindingFactory.Warning("overlapping_listener_bind", $"Multiple enabled listeners share bind identity {group.Key}.", sourceName, "listeners", "Keep only one enabled listener per address, port, and transport."));
        }
    }

    private static void AddHttp3Findings(ProxyConfigLintConfigurationSnapshot snapshot, bool activeRuntime, IReadOnlyList<ProxyConfigLintRuntimeListenerState> runtimeListeners, string? sourceName, List<ConfigLintFinding> findings)
    {
        foreach (var listener in snapshot.Listeners)
        {
            var path = $"listeners[{listener.Name}]";
            if (listener.Http3Configured && !listener.Http3EnabledForTraffic)
            {
                findings.Add(ConfigLintFindingFactory.Warning("http3_configured_not_ready", $"Listener '{listener.Name}' has HTTP/3 configured but it is not ready for traffic: {listener.Http3DisabledReason}.", sourceName, path, "Keep HTTP/3 disabled or satisfy the TLS and certificate requirements."));
            }

            if (listener.Http3EnabledForTraffic && !listener.Http3AltSvcEnabled)
            {
                findings.Add(ConfigLintFindingFactory.Warning("http3_alt_svc_disabled", $"Listener '{listener.Name}' has HTTP/3 {listener.Http3EnablementLevel} enabled but Alt-Svc advertisement is disabled.", sourceName, path, "Enable Http3AltSvcEnabled only after the QUIC listener is reachable."));
            }

            if (listener.Http3AltSvcEnabled && !IsAltSvcReady(activeRuntime, runtimeListeners, listener))
            {
                findings.Add(ConfigLintFindingFactory.Warning("http3_alt_svc_not_ready", $"Listener '{listener.Name}' configures Alt-Svc but no matching active QUIC listener is currently ready.", sourceName, path, "MDRAVA only emits Alt-Svc when the HTTP/3 QUIC listener is active."));
            }
        }
    }

    private static bool IsAltSvcReady(bool activeRuntime, IReadOnlyList<ProxyConfigLintRuntimeListenerState> runtimeListeners, ProxyConfigLintListener listener)
    {
        return activeRuntime && listener.QuicIdentityKey is not null && runtimeListeners.Any(runtime => string.Equals(runtime.Kind, "quic", StringComparison.OrdinalIgnoreCase) && runtime.Active && string.Equals(runtime.Identity, listener.QuicIdentityKey, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddHttpsRedirectFindings(ProxyConfigLintConfigurationSnapshot snapshot, string? sourceName, List<ConfigLintFinding> findings)
    {
        var httpsListenerExists = snapshot.Listeners.Any(static listener => listener.Enabled && string.Equals(listener.Transport, "Https", StringComparison.OrdinalIgnoreCase));
        foreach (var route in snapshot.Routes.Where(route => route.HttpsRedirectEnabled && !httpsListenerExists))
        {
            findings.Add(ConfigLintFindingFactory.Warning("https_redirect_without_https_listener", $"Route '{route.Name}' enables HTTP to HTTPS redirect but no enabled HTTPS listener exists.", sourceName, ConfigLintRouteIdentityPolicy.RoutePath(route), "Add an HTTPS listener or disable the redirect for this route."));
        }
    }
}
