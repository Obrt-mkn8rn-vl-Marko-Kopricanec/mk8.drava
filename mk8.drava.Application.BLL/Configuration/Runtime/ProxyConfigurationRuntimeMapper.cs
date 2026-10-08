using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.Configuration;
public static partial class ProxyConfigurationRuntimeMapper
{
    public static ProxyConfigurationSnapshot ToRuntimeSnapshot(ProxyOptions options, ProxyOperationalOptions operationalOptions, ProxyAdminTokenResolution adminTokenResolution, IReadOnlyDictionary<string, RuntimeCertificate> certificates, int version, DateTimeOffset loadedAtUtc, string sourceDirectory, IReadOnlyList<string> sourceFiles, ProxyConfigurationDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(options);
        var listeners = ToRuntimeListeners(options.Listeners);
        var routes = ToRuntimeRoutes(options.Routes, operationalOptions);
        var timeouts = new RuntimeTimeouts(TimeSpan.FromMilliseconds(operationalOptions.Timeouts.ClientRequestHeadTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.ClientRequestBodyIdleTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.UpstreamConnectTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.UpstreamResponseHeadTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.UpstreamResponseBodyIdleTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.DownstreamWriteTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.TlsHandshakeTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.ClientKeepAliveIdleTimeoutMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.UpstreamIdleConnectionLifetimeMs), TimeSpan.FromMilliseconds(operationalOptions.Timeouts.TunnelIdleTimeoutMs));
        var connectionLimits = new RuntimeConnectionLimits(operationalOptions.Connections.MaxRequestsPerClientConnection, operationalOptions.Connections.MaxIdleUpstreamConnectionsPerUpstream, operationalOptions.Connections.MaxActiveUpgradedTunnels);
        var observability = new RuntimeObservabilityOptions(operationalOptions.Observability.AccessLogEnabled, operationalOptions.Observability.RecentDiagnosticsCapacity, new RuntimeLogPersistenceOptions(operationalOptions.Observability.LogPersistence.AccessLogEnabled, operationalOptions.Observability.LogPersistence.AdminAuditEnabled, operationalOptions.Observability.LogPersistence.MaxFileBytes, operationalOptions.Observability.LogPersistence.MaxFiles));
        var limits = new RuntimeLimits(operationalOptions.Limits.MaxActiveClientConnections, operationalOptions.Limits.MaxConcurrentTlsHandshakes, operationalOptions.Limits.RequestsPerMinutePerIp, operationalOptions.Limits.UpgradeRequestsPerMinutePerIp, operationalOptions.Limits.MaxRequestHeadBytes, operationalOptions.Limits.MaxHeaderCount, operationalOptions.Limits.MaxHeaderLineBytes, operationalOptions.Limits.MaxRequestBodyBytes, operationalOptions.Limits.MaxPathBytes, TimeSpan.FromSeconds(operationalOptions.Limits.ShutdownGracePeriodSeconds));
        var forwardedHeaders = new RuntimeForwardedHeadersOptions(operationalOptions.ForwardedHeaders.Enabled, operationalOptions.ForwardedHeaders.TrustedProxies.Where(static entry => !string.IsNullOrWhiteSpace(entry)).Select(static entry => entry.Trim()).ToArray());
        var adminSecurity = ToRuntimeAdminSecurityOptions(operationalOptions.Admin, adminTokenResolution);
        var acme = ToRuntimeAcmeOptions(operationalOptions.Acme);
        var metrics = ToRuntimeMetricsOptions(operationalOptions.Metrics);
        return new ProxyConfigurationSnapshot(version, loadedAtUtc, sourceDirectory, sourceFiles, discovery, adminSecurity, acme, timeouts, connectionLimits, observability, limits, forwardedHeaders, certificates, listeners, routes, metrics);
    }

    public static IReadOnlyList<RuntimeListener> ToRuntimeListeners(IEnumerable<ListenerOptions> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        return listeners.Select(static listener =>
        {
            var http3Compatibility = RuntimeHttp3Compatibility.From(listener);
            return new RuntimeListener(listener.Name, listener.Address, listener.Port, listener.Enabled, ParseTransport(listener.Transport), string.IsNullOrWhiteSpace(listener.DefaultCertificateId) ? null : listener.DefaultCertificateId, listener.SniCertificates.Select(static binding => new RuntimeSniCertificateBinding(binding.HostName, binding.CertificateId)).ToArray(), listener.Backlog, listener.MaxRequestHeadBytes, listener.MaxResponseHeadBytes, listener.MaxChunkLineBytes, listener.ForwardingBufferBytes, http3Compatibility.Protocols, http3Compatibility.EffectiveEnablement, new RuntimeHttp3AltSvcOptions(listener.Http3AltSvcEnabled, listener.Http3AltSvcMaxAgeSeconds), new RuntimeHttp2Limits(listener.Http2MaxConcurrentStreams, listener.Http2MaxHeaderListBytes, listener.Http2MaxFrameSize));
        }).ToArray();
    }

    public static IReadOnlyList<RuntimeRoute> ToRuntimeRoutes(IEnumerable<ProxyRouteOptions> routes, ProxyOperationalOptions operationalOptions)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(operationalOptions);
        return routes.Select(route => ToRuntimeRoute(route, operationalOptions)).ToArray();
    }

    public static RuntimeAdminSecurityOptions ToRuntimeAdminSecurityOptions(ProxyAdminOptions options, ProxyAdminTokenResolution resolvedToken)
    {
        ArgumentNullException.ThrowIfNull(resolvedToken);
        ArgumentNullException.ThrowIfNull(options);
        return new RuntimeAdminSecurityOptions(ProxyAdminSecurityTokenPolicy.NormalizeUrls(options.Urls), options.RequireAuthentication, !string.IsNullOrEmpty(resolvedToken.Token), resolvedToken.Token, resolvedToken.TokenEnvironmentVariable, resolvedToken.TokenSource, options.RecentAuditCapacity);
    }

    public static RuntimeAcmeOptions ToRuntimeAcmeOptions(ProxyAcmeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directoryUrl = ResolveAcmeDirectoryUrl(options);
        return new RuntimeAcmeOptions(options.Enabled, options.UseStaging, directoryUrl, options.ContactEmails.Where(static contact => !string.IsNullOrWhiteSpace(contact)).Select(static contact => contact.Trim()).ToArray(), options.TermsAccepted, string.IsNullOrWhiteSpace(options.StoragePath) ? "acme" : options.StoragePath.Trim(), options.RenewBeforeDays, options.CheckIntervalMinutes, options.RetryAfterMinutes, options.Certificates.Select(certificate => new RuntimeAcmeCertificateOptions(certificate.Id, certificate.Enabled, certificate.Domains.Where(static domain => !string.IsNullOrWhiteSpace(domain)).Select(static domain => domain.Trim().ToLowerInvariant()).ToArray(), certificate.RenewBeforeDays ?? options.RenewBeforeDays)).ToArray());
    }

    public static RuntimeMetricsOptions ToRuntimeMetricsOptions(ProxyMetricsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new RuntimeMetricsOptions(options.Enabled, RuntimeMetricsOptions.FixedAdminEndpointPath, ProtectedByAdminAuth: true, options.IncludePerRouteLabels, options.IncludePerUpstreamLabels, options.PublicMetricsEnabled);
    }

    public static string ResolveAcmeDirectoryUrl(ProxyAcmeOptions options)
    {
        return ProxyAcmeDirectoryPolicy.ResolveDirectoryUrl(options);
    }

    private static RuntimeListenerTransport ParseTransport(string transport)
    {
        return string.Equals(transport, "https", StringComparison.OrdinalIgnoreCase) ? RuntimeListenerTransport.Https : RuntimeListenerTransport.Http;
    }

    private static RuntimeRoute ToRuntimeRoute(ProxyRouteOptions route, ProxyOperationalOptions operationalOptions)
    {
        var action = ParseAction(route.Action);
        return new RuntimeRoute(route.Name, route.Host, route.PathPrefix, action, string.IsNullOrWhiteSpace(route.LoadBalancingPolicy) ? "round-robin" : route.LoadBalancingPolicy, new RuntimeHealthCheckOptions(route.HealthCheck.Enabled, string.IsNullOrWhiteSpace(route.HealthCheck.Path) ? "/health" : route.HealthCheck.Path, TimeSpan.FromSeconds(route.HealthCheck.IntervalSeconds), TimeSpan.FromSeconds(route.HealthCheck.TimeoutSeconds), route.HealthCheck.HealthyThreshold, route.HealthCheck.UnhealthyThreshold), route.Upstreams.Select(upstream => new RuntimeUpstream(route.Name, upstream.Name, string.IsNullOrWhiteSpace(upstream.Scheme) ? "http" : upstream.Scheme.ToLowerInvariant(), string.IsNullOrWhiteSpace(upstream.Protocol) ? RuntimeUpstreamProtocol.Http1 : upstream.Protocol.Trim().ToLowerInvariant(), upstream.Address, upstream.Port, upstream.Weight, new RuntimeUpstreamTlsOptions(upstream.UpstreamTls.ValidateCertificate, string.IsNullOrWhiteSpace(upstream.UpstreamTls.SniHost) ? null : upstream.UpstreamTls.SniHost.Trim()), ToRuntimeCircuitBreaker(upstream.CircuitBreaker))).ToArray(), new RuntimeHttpsRedirectPolicy(route.HttpsRedirect.Enabled ?? false, route.HttpsRedirect.StatusCode ?? 308, route.HttpsRedirect.HttpsPort), new RuntimeCanonicalHostPolicy(route.CanonicalHost.Enabled ?? !string.IsNullOrWhiteSpace(route.CanonicalHost.TargetHost), route.CanonicalHost.TargetHost, route.CanonicalHost.StatusCode ?? 308), new RuntimeHeaderPolicy(route.HeaderPolicy.SetRequestHeaders.Select(static header => new ProxyHeaderField(header.Name, header.Value)).ToArray(), route.HeaderPolicy.RemoveRequestHeaders.ToArray(), route.HeaderPolicy.SetResponseHeaders.Select(static header => new ProxyHeaderField(header.Name, header.Value)).ToArray(), route.HeaderPolicy.RemoveResponseHeaders.ToArray()), new RuntimePathRewritePolicy(route.PathRewrite.StripPrefix, route.PathRewrite.ReplacePrefix, route.PathRewrite.Replacement), new RuntimeRedirectPolicy(route.Redirect.StatusCode ?? 308, route.Redirect.TargetUrl, route.Redirect.TargetPath, route.Redirect.PreserveQuery), new RuntimeStaticResponse(route.StaticResponse.StatusCode, route.StaticResponse.ContentType, route.StaticResponse.Body), new RuntimeMaintenancePolicy(route.Maintenance.Enabled ?? false, route.Maintenance.RetryAfterSeconds, route.Maintenance.ContentType, route.Maintenance.Body), new RuntimeCachePolicy(route.Cache.Enabled, route.Cache.MaxEntryBytes, route.Cache.MaxTotalBytes, TimeSpan.FromSeconds(route.Cache.DefaultTtlSeconds), route.Cache.RespectOriginCacheControl, route.Cache.VaryByHeaders.Where(static header => !string.IsNullOrWhiteSpace(header)).Select(static header => header.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), route.Cache.CacheableStatusCodes.Distinct().Order().ToArray(), route.Cache.Methods.Where(static method => !string.IsNullOrWhiteSpace(method)).Select(static method => method.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()), new RuntimeRouteResolvedOptions(route.Overrides.MaxRequestBodyBytes ?? operationalOptions.Limits.MaxRequestBodyBytes, TimeSpan.FromMilliseconds(route.Overrides.ClientRequestHeadTimeoutMs ?? operationalOptions.Timeouts.ClientRequestHeadTimeoutMs), TimeSpan.FromMilliseconds(route.Overrides.UpstreamResponseHeadTimeoutMs ?? operationalOptions.Timeouts.UpstreamResponseHeadTimeoutMs), route.Overrides.AccessLogEnabled ?? operationalOptions.Observability.AccessLogEnabled), route.SiteName, ToRuntimeRetry(route.Retry));
    }

    private static RuntimeRetryPolicy ToRuntimeRetry(ProxyRetryPolicyOptions retry)
    {
        return new RuntimeRetryPolicy(retry.Enabled, retry.MaxAttempts, retry.PerAttemptTimeoutMs.HasValue && retry.PerAttemptTimeoutMs.Value > 0 ? TimeSpan.FromMilliseconds(retry.PerAttemptTimeoutMs.Value) : null, retry.RetryOnConnectFailure, retry.RetryOnUpstreamResponseHeadTimeout, retry.RetryOnStatusCodes.Distinct().Order().ToArray(), retry.RetryMethods.Where(static method => !string.IsNullOrWhiteSpace(method)).Select(static method => method.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), TimeSpan.FromMilliseconds(retry.RetryBackoffMilliseconds));
    }

    private static RuntimeCircuitBreakerPolicy ToRuntimeCircuitBreaker(ProxyCircuitBreakerOptions circuitBreaker)
    {
        return new RuntimeCircuitBreakerPolicy(circuitBreaker.Enabled, circuitBreaker.FailureThreshold, TimeSpan.FromSeconds(circuitBreaker.SamplingWindowSeconds), TimeSpan.FromSeconds(circuitBreaker.OpenDurationSeconds), circuitBreaker.HalfOpenMaxAttempts, circuitBreaker.FailureStatusCodes.Distinct().Order().ToArray());
    }

    private static RuntimeRouteAction ParseAction(string action)
    {
        if (string.Equals(action, "redirect", StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeRouteAction.Redirect;
        }

        if (string.Equals(action, "staticResponse", StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeRouteAction.StaticResponse;
        }

        return RuntimeRouteAction.Proxy;
    }
}
