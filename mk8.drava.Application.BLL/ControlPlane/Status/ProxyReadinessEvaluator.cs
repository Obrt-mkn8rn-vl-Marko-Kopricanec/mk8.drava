namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyReadinessEvaluator
{
    public static ProxyReadinessStatus Evaluate(ProxyReadinessEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var subsystems = input.Subsystems;
        List<string> notReadyReasons = [];
        List<string> degradedReasons = [];
        if (!input.HasActiveConfiguration)
        {
            notReadyReasons.Add("config_missing");
        }

        if (input.IsShuttingDown)
        {
            notReadyReasons.Add("shutdown_in_progress");
        }

        if (subsystems.Listeners.Enabled == 0)
        {
            notReadyReasons.Add("no_enabled_listeners");
        }
        else if (subsystems.Listeners.Active == 0)
        {
            notReadyReasons.Add("no_active_listeners");
        }

        if (subsystems.Listeners.Failed > 0)
        {
            degradedReasons.Add("listener_start_failed");
        }

        if (input.LastListenerReloadFailed)
        {
            degradedReasons.Add("last_listener_reload_failed");
        }

        if (string.Equals(input.LogPersistenceState, ProxyStatusText.Degraded, StringComparison.OrdinalIgnoreCase))
        {
            degradedReasons.Add("log_persistence_degraded");
        }

        if (string.Equals(input.RuntimePreflight.State, ProxyStatusText.Failed, StringComparison.OrdinalIgnoreCase))
        {
            notReadyReasons.Add("runtime_preflight_failed");
        }
        else if (string.Equals(input.RuntimePreflight.State, ProxyStatusText.Degraded, StringComparison.OrdinalIgnoreCase))
        {
            degradedReasons.Add("runtime_preflight_degraded");
        }

        if (subsystems.Upstreams.Unhealthy > 0)
        {
            degradedReasons.Add("upstream_unhealthy");
        }

        if (subsystems.Upstreams.HealthChecksEnabled > 0 && subsystems.Upstreams.Unhealthy == subsystems.Upstreams.HealthChecksEnabled)
        {
            degradedReasons.Add("all_health_checked_upstreams_unhealthy");
        }

        if (subsystems.Circuits.Open > 0 || subsystems.Circuits.HalfOpen > 0)
        {
            degradedReasons.Add("circuit_not_closed");
        }

        if (subsystems.Certificates.MissingReferences > 0)
        {
            degradedReasons.Add("certificate_reference_missing");
        }

        if (subsystems.Certificates.Expired > 0)
        {
            degradedReasons.Add("certificate_expired");
        }

        if (subsystems.Certificates.NotYetValid > 0)
        {
            degradedReasons.Add("certificate_not_yet_valid");
        }

        if (subsystems.Certificates.ExpiringSoon > 0)
        {
            degradedReasons.Add("certificate_expiring_soon");
        }

        if (subsystems.Acme.Failed > 0 || subsystems.Acme.RenewalBackoff > 0)
        {
            degradedReasons.Add("acme_degraded");
        }

        if (subsystems.Protocols.ClientHttp3Enabled && !subsystems.Protocols.ClientHttp3Ready)
        {
            degradedReasons.Add("http3_not_ready");
        }

        var state = notReadyReasons.Count > 0 ? ProxyStatusText.NotReady : degradedReasons.Count > 0 ? ProxyStatusText.Degraded : ProxyStatusText.Healthy;
        var reasons = notReadyReasons.Count > 0 ? notReadyReasons : degradedReasons;
        return ProxyReadinessStatus.Evaluated(state, reasons, input.EvaluatedAtUtc, input.ConfigGeneration);
    }
}
