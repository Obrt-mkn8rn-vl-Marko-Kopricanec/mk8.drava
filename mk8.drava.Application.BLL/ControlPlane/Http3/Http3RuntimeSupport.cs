using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class Http3RuntimeSupport
{
    public static RuntimeHttp3SupportProjection ProjectConfiguration(Http3SupportConfigurationSource source, RuntimeHttp3PlatformSupport platformSupport)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(platformSupport);
        return ProjectCore(source, platformSupport, [], hasRuntimeState: false);
    }

    public static RuntimeHttp3SupportProjection ProjectRuntime(Http3SupportConfigurationSource source, RuntimeHttp3PlatformSupport platformSupport, IReadOnlyList<Http3SupportRuntimeListenerSource> runtimeListeners)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(platformSupport);
        ArgumentNullException.ThrowIfNull(runtimeListeners);
        return ProjectCore(source, platformSupport, Http3List.Copy(runtimeListeners), hasRuntimeState: true);
    }

    private static RuntimeHttp3SupportProjection ProjectCore(Http3SupportConfigurationSource source, RuntimeHttp3PlatformSupport platformSupport, IReadOnlyList<Http3SupportRuntimeListenerSource> runtimeListeners, bool hasRuntimeState)
    {
        var listeners = source.Listeners;
        var http3Configured = listeners.Any(static listener => listener.Configured);
        var http3Enabled = listeners.Any(static listener => listener.EnabledForTraffic);
        var quicReady = runtimeListeners.Any(static listener => listener.IsQuic && listener.State == ProxyListenerState.Active);
        var altSvcConfigured = listeners.Any(static listener => listener.AltSvcEnabled);
        var altSvcActive = altSvcConfigured && hasRuntimeState && listeners.Any(listener => HasActiveQuicListener(listener, runtimeListeners));
        var maxAge = listeners.Where(static listener => listener.AltSvcEnabled).Select(static listener => (int? )listener.AltSvcMaxAgeSeconds).FirstOrDefault();
        var blockers = DefaultReadinessBlockers(listeners, platformSupport);
        var defaultState = !http3Configured ? "disabled" : blockers.Length == 0 && http3Enabled ? "default-enabled" : "explicit";
        var readinessConclusion = string.Equals(defaultState, "default-enabled", StringComparison.Ordinal) ? "default_enabled_for_eligible_tls_proxy_listeners" : string.Equals(defaultState, "disabled", StringComparison.Ordinal) ? "disabled" : "explicit_only";
        var upstreamHttp3Configured = source.UpstreamHttp3Configured;
        return new RuntimeHttp3SupportProjection(RuntimeSupport: platformSupport.RuntimeSupport, QuicListenerSupported: platformSupport.QuicListenerSupported, QuicConnectionSupported: platformSupport.QuicConnectionSupported, Configured: ConfiguredMode(listeners), EnablementLevel: EnablementLevel(listeners), EnabledForTraffic: http3Enabled, QuicListenerReady: quicReady, AltSvcConfigured: altSvcConfigured, AltSvcActive: altSvcActive, AltSvcMaxAgeSeconds: maxAge, DisabledReason: DisabledReason(listeners, http3Configured, http3Enabled, quicReady, hasRuntimeState), UdpQuicListenerIdentityModeled: true, ReadinessConclusion: readinessConclusion, DefaultEnablementState: defaultState, DefaultReadinessBlockers: blockers, AltSvcStateReason: AltSvcReason(altSvcConfigured, altSvcActive, http3Enabled, quicReady, hasRuntimeState), QpackMode: "static_with_zero_dynamic_table", QpackDynamicTableCapacity: 0, QpackBlockedStreams: 0, RequestBodyMode: "streaming", ClientHttp3SupportLevel: "default_enabled_for_eligible_tls_proxy_listeners", UpstreamHttp3SupportLevel: upstreamHttp3Configured ? "opt_in_https_quic_reused_multiplexed" : "opt_in_https_quic_available", ClientProtocols: ["http1", "http2", "http3"], UpstreamProtocols: ["http1", "http2", "http3"], SupportedRouteActions: ["proxy", "redirect", "staticResponse", "maintenance"], SupportedPolicyFeatures: ["cache_get_head", "retry_circuit_safe_methods", "weighted_balancing", "health_checks", "path_rewrites", "canonical_redirects", "http_to_https_redirects", "forwarded_headers", "request_response_header_policies"], UnsupportedFeatures: RuntimeHttp3UnsupportedFeatureCodes.EffectiveConfig, UpstreamHttp3Configured: upstreamHttp3Configured, UpstreamPoolingMode: upstreamHttp3Configured ? "reused_multiplexed" : "not_configured", UpstreamMultiplexingEnabled: upstreamHttp3Configured, UpstreamMaxStreamsPerConnection: upstreamHttp3Configured ? 8 : 0, UpstreamQpackMode: "static_with_zero_dynamic_table", UpstreamPoolingLimitationReason: "");
    }

    private static string EnablementLevel(IReadOnlyList<Http3SupportListenerSource> listeners)
    {
        return listeners.Any(static listener => listener.Configured && string.Equals(listener.EnablementLevel, "default", StringComparison.Ordinal)) ? "default" : "disabled";
    }

    private static string DisabledReason(IReadOnlyList<Http3SupportListenerSource> listeners, bool configured, bool enabled, bool ready, bool hasRuntimeState)
    {
        if (ready)
        {
            return "quic_listener_ready";
        }

        if (enabled && !hasRuntimeState)
        {
            return "default_enabled";
        }

        if (enabled)
        {
            return "configured_but_listener_not_ready";
        }

        return configured ? "configured_but_inactive" : "not_configured";
    }

    private static string[] DefaultReadinessBlockers(IReadOnlyList<Http3SupportListenerSource> listeners, RuntimeHttp3PlatformSupport platformSupport)
    {
        List<string> blockers = [];
        if (!platformSupport.QuicListenerSupported || !platformSupport.QuicConnectionSupported)
        {
            blockers.Add("runtime_quic_unsupported");
        }

        if (!listeners.Any(static listener => listener.EnabledForTraffic))
        {
            blockers.Add("no_http3_enabled_listener");
        }

        return blockers.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ConfiguredMode(IReadOnlyList<Http3SupportListenerSource> listeners)
    {
        return listeners.Any(static listener => listener.Configured && string.Equals(listener.EnablementLevel, "default", StringComparison.Ordinal)) ? "default" : "disabled";
    }

    private static bool HasActiveQuicListener(Http3SupportListenerSource listener, IReadOnlyList<Http3SupportRuntimeListenerSource> runtimeListeners)
    {
        return listener.AltSvcEnabled && !string.IsNullOrWhiteSpace(listener.QuicListenerIdentity) && runtimeListeners.Any(candidate => candidate.IsQuic && candidate.State == ProxyListenerState.Active && string.Equals(candidate.Identity, listener.QuicListenerIdentity, StringComparison.OrdinalIgnoreCase));
    }

    private static string AltSvcReason(bool configured, bool active, bool enabled, bool ready, bool hasRuntimeState)
    {
        if (active)
        {
            return "active";
        }

        if (!configured)
        {
            return "not_configured";
        }

        if (!enabled)
        {
            return "http3_not_enabled";
        }

        if (!hasRuntimeState)
        {
            return "runtime_state_unavailable";
        }

        return ready ? "listener_not_matched" : "quic_listener_not_ready";
    }
}
