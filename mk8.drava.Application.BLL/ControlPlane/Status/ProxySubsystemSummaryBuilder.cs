using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static partial class ProxySubsystemSummaryBuilder
{
    public static ProxyCertificateSubsystemSummary BuildCertificates(ProxyCertificateSummarySource? source, DateTimeOffset now)
    {
        if (source is null)
        {
            return ProxyCertificateSubsystemSummary.Unknown;
        }

        HashSet<string> referenced = new(source.ReferencedCertificateIds, StringComparer.OrdinalIgnoreCase);
        HashSet<string> loadedIds = new(source.LoadedCertificates.Select(static certificate => certificate.Id), StringComparer.OrdinalIgnoreCase);
        var missing = referenced.Where(certificateId => !loadedIds.Contains(certificateId)).OrderBy(static certificateId => certificateId, StringComparer.OrdinalIgnoreCase).ToArray();
        var expired = source.LoadedCertificates.Where(certificate => certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime).OrderByDescending(certificate => certificate.NotAfter.ToUniversalTime()).ToArray();
        var notYetValid = source.LoadedCertificates.Where(certificate => certificate.NotBefore.ToUniversalTime() > now.UtcDateTime).OrderBy(certificate => certificate.NotBefore.ToUniversalTime()).ToArray();
        var expiringSoon = source.LoadedCertificates.Where(certificate => certificate.NotAfter.ToUniversalTime() > now.UtcDateTime && certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime.AddDays(30)).OrderBy(certificate => certificate.NotAfter.ToUniversalTime()).ToArray();
        return new ProxyCertificateSubsystemSummary(referenced.Count, source.LoadedCertificates.Count, missing.Length, expired.Length, notYetValid.Length, expiringSoon.Length, BuildCertificateLastIssue(missing, expired, notYetValid, expiringSoon, now));
    }

    public static ProxyAcmeSubsystemSummary BuildAcme(ProxyAcmeSummaryConfigurationSource? configuration, IReadOnlyList<AcmeCertificateLifecycleStatus> acmeStatuses, DateTimeOffset now)
    {
        if (configuration is null)
        {
            return ProxyAcmeSubsystemSummary.Unknown;
        }

        var activeFailures = acmeStatuses.Where(IsCurrentAcmeFailure).OrderByDescending(static status => status.LastFailedAtUtc).ToArray();
        var renewalBackoffs = acmeStatuses.Where(status => status.NextAttemptNotBeforeUtc.HasValue && status.NextAttemptNotBeforeUtc > now).OrderByDescending(static status => status.NextAttemptNotBeforeUtc).ToArray();
        return new ProxyAcmeSubsystemSummary(configuration.Enabled, configuration.ConfiguredCertificates, acmeStatuses.Count(static status => status.Active), activeFailures.Length, renewalBackoffs.Length, BuildAcmeLastIssue(activeFailures, renewalBackoffs));
    }

    private static ProxySubsystemIssueSummary? BuildCertificateLastIssue(string[] missing, ProxyCertificateValiditySource[] expired, ProxyCertificateValiditySource[] notYetValid, ProxyCertificateValiditySource[] expiringSoon, DateTimeOffset now)
    {
        if (missing.Length > 0)
        {
            return Issue(now, "certificate", "missing_reference", missing[0]);
        }

        if (expired.Length > 0)
        {
            var certificate = expired[0];
            return Issue(CertificateTime(certificate.NotAfter), "certificate", "expired", certificate.Id);
        }

        if (notYetValid.Length > 0)
        {
            var certificate = notYetValid[0];
            return Issue(CertificateTime(certificate.NotBefore), "certificate", "not_yet_valid", certificate.Id);
        }

        if (expiringSoon.Length > 0)
        {
            var certificate = expiringSoon[0];
            return Issue(CertificateTime(certificate.NotAfter), "certificate", "expiring_soon", certificate.Id);
        }

        return null;
    }

    private static ProxySubsystemIssueSummary? BuildAcmeLastIssue(AcmeCertificateLifecycleStatus[] activeFailures, AcmeCertificateLifecycleStatus[] renewalBackoffs)
    {
        if (activeFailures.Length > 0)
        {
            var status = activeFailures[0];
            return Issue(status.LastFailedAtUtc!.Value, "acme", NormalizeAcmeReason(status.LastResult), status.CertificateId);
        }

        if (renewalBackoffs.Length > 0)
        {
            var status = renewalBackoffs[0];
            return Issue(status.NextAttemptNotBeforeUtc!.Value, "acme", "renewal_backoff", status.CertificateId);
        }

        return null;
    }

    private static bool IsCurrentAcmeFailure(AcmeCertificateLifecycleStatus status)
    {
        return status.LastFailedAtUtc.HasValue && (!status.LastSucceededAtUtc.HasValue || status.LastFailedAtUtc.Value > status.LastSucceededAtUtc.Value);
    }

    private static ProxySubsystemIssueSummary Issue(DateTimeOffset timestampUtc, string category, string reason, string? affectedIdentity)
    {
        return new ProxySubsystemIssueSummary(timestampUtc.ToUniversalTime(), category, reason, SafeIdentity(affectedIdentity));
    }

    private static DateTimeOffset CertificateTime(DateTime value)
    {
        return new DateTimeOffset(value.ToUniversalTime());
    }

    private static string NormalizeAcmeReason(string? value)
    {
        #pragma warning disable CA1308 // Normalize the imported status vocabulary to canonical lower-case labels and preserve unknown-value classification.
        return value?.Trim().ToLowerInvariant() switch
        #pragma warning restore CA1308
        {
            "attempting" => "attempting",
            "disabled" => ProxyStatusText.Disabled,
            "failed" => ProxyStatusText.Failed,
            "not-due" => "not_due",
            "succeeded" => "succeeded",
            _ => ProxyStatusText.Unknown
        };
    }

    private static string? SafeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[Math.Min(value.Length, 128)];
        var length = 0;
        foreach (var character in value.Trim())
        {
            if (length >= buffer.Length)
            {
                break;
            }

            buffer[length++] = char.IsControl(character) ? '_' : character;
        }

        return new string (buffer[..length]);
    }

    public static ProxyConfigSubsystemSummary BuildConfig(bool active, int? generation, DateTimeOffset? loadedAtUtc, bool? lastListenerReloadSucceeded)
    {
        return new ProxyConfigSubsystemSummary(active, generation, loadedAtUtc, lastListenerReloadSucceeded, lastListenerReloadSucceeded is null ? null : lastListenerReloadSucceeded.Value ? "listener_reload_succeeded" : "listener_reload_failed");
    }

    public static ProxyListenerSubsystemSummary BuildListeners(IReadOnlyList<ProxyConfiguredListenerSummarySource> configuredListeners, IReadOnlyList<ProxyRuntimeListenerSummarySource> runtimeListeners)
    {
        ArgumentNullException.ThrowIfNull(configuredListeners);
        return new ProxyListenerSubsystemSummary(configuredListeners.Count, configuredListeners.Count(static listener => listener.Enabled), runtimeListeners.Count(static listener => listener.State == ProxyListenerState.Active), runtimeListeners.Count(static listener => listener.State == ProxyListenerState.Failed), runtimeListeners.Count(static listener => listener.State == ProxyListenerState.Draining), configuredListeners.Count(static listener => listener.Enabled && listener.Http1Enabled), configuredListeners.Count(static listener => listener.Enabled && listener.Http2Enabled), configuredListeners.Count(static listener => listener.Enabled && listener.Http3EnabledForTraffic), runtimeListeners.Count(static listener => listener.IsQuic && listener.State == ProxyListenerState.Active));
    }

    public static ProxyRouteSubsystemSummary BuildRoutes(IReadOnlyList<ProxyRouteSummarySource> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return new ProxyRouteSubsystemSummary(routes.Select(static route => route.SiteName).Distinct(StringComparer.OrdinalIgnoreCase).Count(), routes.Count, routes.Count(static route => route.IsProxyRoute), routes.Count(static route => !route.IsProxyRoute), routes.Count(static route => route.CacheEnabled));
    }

    public static ProxyUpstreamSubsystemSummary BuildUpstreams(IReadOnlyList<ProxyUpstreamSummarySource> upstreams)
    {
        ArgumentNullException.ThrowIfNull(upstreams);
        return new ProxyUpstreamSubsystemSummary(upstreams.Count, upstreams.Count(static upstream => upstream.HealthState == UpstreamHealthState.Healthy), upstreams.Count(static upstream => upstream.HealthState == UpstreamHealthState.Unhealthy), upstreams.Count(static upstream => upstream.HealthState == UpstreamHealthState.Unknown), upstreams.Count(static upstream => upstream.HealthCheckEnabled));
    }

    public static ProxyCacheSubsystemSummary BuildCache(int enabledRoutes, ProxyCacheStatus? cacheStatus)
    {
        return new ProxyCacheSubsystemSummary(enabledRoutes > 0, enabledRoutes, cacheStatus?.EntryCount ?? 0, cacheStatus?.ApproximateBytes ?? 0);
    }

    public static ProxyCircuitSubsystemSummary BuildCircuits(IReadOnlyList<ProxyUpstreamSummarySource> upstreams)
    {
        return new ProxyCircuitSubsystemSummary(upstreams.Count(static upstream => upstream.CircuitBreakerEnabled), upstreams.Count(static upstream => upstream.CircuitBreakerState == CircuitBreakerRuntimeState.Open), upstreams.Count(static upstream => upstream.CircuitBreakerState == CircuitBreakerRuntimeState.HalfOpen), upstreams.Count(static upstream => upstream.CircuitBreakerState == CircuitBreakerRuntimeState.Closed));
    }

    public static ProxyLimitSubsystemSummary BuildLimits(ProxyLimitConfigurationSummarySource? configuration, ProxyLimitRuntimeSummarySource runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return new ProxyLimitSubsystemSummary(configuration?.MaxActiveClientConnections ?? 0, runtime.ActiveConnections, configuration?.MaxConcurrentTlsHandshakes ?? 0, runtime.ActiveTlsHandshakes, runtime.ActiveHttp2Streams, runtime.ActiveHttp3Streams, runtime.ActiveUpstreamHttp3Streams, configuration?.RequestsPerMinutePerIp ?? 0);
    }

    public static ProxyLogSubsystemSummary BuildLogs(ProxyLogSummarySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ProxyLogSubsystemSummary(source.AccessLogPersistenceEnabled, source.AdminAuditPersistenceEnabled, source.State, source.Reason);
    }

    public static ProxyShutdownSubsystemSummary BuildShutdown(ProxyShutdownSummarySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ProxyShutdownSubsystemSummary(source.IsRunning, source.IsShuttingDown, source.ShutdownStartedAtUtc, source.ShutdownDeadlineUtc);
    }

    public static ProxyProtocolSubsystemSummary BuildProtocols(IReadOnlyList<ProxyConfiguredListenerSummarySource> listeners, bool clientHttp3Enabled, bool clientHttp3Ready, IReadOnlyList<ProxyRouteSummarySource> routes)
    {
        return new ProxyProtocolSubsystemSummary(listeners.Any(static listener => listener.Enabled && listener.Http1Enabled), listeners.Any(static listener => listener.Enabled && listener.Http2Enabled), clientHttp3Enabled, clientHttp3Ready, routes.Any(static route => route.HasHttp3Upstream), RuntimeHttp3UnsupportedFeatureCodes.StatusSummary);
    }
}
