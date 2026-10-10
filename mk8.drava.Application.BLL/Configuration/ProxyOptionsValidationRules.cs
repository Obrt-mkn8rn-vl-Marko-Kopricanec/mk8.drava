using Mk8.Drava.Application.BLL.Http;
using System.Text;

namespace Mk8.Drava.Application.BLL.Configuration;
public static partial class ProxyOptionsValidationRules
{
    private static void ValidateRoutes(List<string> failures, ProxyOptions options, IProxyEndpointAddressPolicy endpointAddressPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy)
    {
        if (options.Routes.Count == 0)
        {
            failures.Add("Proxy:Routes must contain at least one route.");
        }

        HashSet<string> routeNames = new(StringComparer.OrdinalIgnoreCase);
        for (var routeIndex = 0; routeIndex < options.Routes.Count; routeIndex++)
        {
            var route = options.Routes[routeIndex];
            var routePrefix = $"Proxy:Routes:{routeIndex}";
            if (string.IsNullOrWhiteSpace(route.Name))
            {
                failures.Add($"{routePrefix}:Name is required.");
            }
            else if (!routeNames.Add(route.Name))
            {
                failures.Add($"{routePrefix}:Name '{route.Name}' is duplicated.");
            }

            if (string.IsNullOrWhiteSpace(route.Host))
            {
                failures.Add($"{routePrefix}:Host is required.");
            }

            if (string.IsNullOrWhiteSpace(route.PathPrefix) || !route.PathPrefix.StartsWith('/'))
            {
                failures.Add($"{routePrefix}:PathPrefix must start with '/'.");
            }

            var routeAction = string.IsNullOrWhiteSpace(route.Action) ? "proxy" : route.Action;
            if (!IsRouteAction(routeAction))
            {
                failures.Add($"{routePrefix}:Action must be 'proxy', 'redirect', or 'staticResponse'.");
            }

            if (IsProxyAction(routeAction) && !string.Equals(route.LoadBalancingPolicy, "round-robin", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{routePrefix}:LoadBalancingPolicy must be 'round-robin' for Phase 8.");
            }

            if (IsProxyAction(routeAction))
            {
                ValidateHealthCheck(failures, routePrefix, route.HealthCheck);
            }

            ValidateRedirectPolicy(failures, routePrefix, route.HttpsRedirect);
            ValidateCanonicalHost(failures, routePrefix, route.CanonicalHost);
            ProxyHeaderPolicyOptionsValidationRules.Validate(failures, routePrefix, route.HeaderPolicy);
            ValidatePathRewrite(failures, routePrefix, route.PathRewrite);
            ValidateMaintenance(failures, routePrefix, route.Maintenance);
            ValidateCachePolicy(failures, routePrefix, route.Cache, routeAction);
            ValidateRetryPolicy(failures, routePrefix, route.Retry, routeAction);
            ValidateOverrides(failures, routePrefix, route.Overrides);
            if (IsProxyAction(routeAction) && route.Upstreams.Count == 0)
            {
                failures.Add($"{routePrefix}:Upstreams must contain at least one upstream.");
            }

            if (IsRedirectAction(routeAction))
            {
                ValidateRedirectRoute(failures, routePrefix, route.Redirect, urlSyntaxPolicy);
            }

            if (IsStaticResponseAction(routeAction))
            {
                ValidateStaticResponse(failures, routePrefix, route.StaticResponse);
            }

            ValidateUpstreams(failures, routePrefix, route, endpointAddressPolicy);
        }
    }

    private static void ValidateUpstreams(List<string> failures, string routePrefix, ProxyRouteOptions route, IProxyEndpointAddressPolicy endpointAddressPolicy)
    {
        HashSet<string> upstreamNames = new(StringComparer.OrdinalIgnoreCase);
        for (var upstreamIndex = 0; upstreamIndex < route.Upstreams.Count; upstreamIndex++)
        {
            var upstream = route.Upstreams[upstreamIndex];
            var upstreamPrefix = $"{routePrefix}:Upstreams:{upstreamIndex}";
            if (string.IsNullOrWhiteSpace(upstream.Name))
            {
                failures.Add($"{upstreamPrefix}:Name is required.");
            }
            else if (!upstreamNames.Add(upstream.Name))
            {
                failures.Add($"{upstreamPrefix}:Name '{upstream.Name}' is duplicated within route '{route.Name}'.");
            }

            if (string.IsNullOrWhiteSpace(upstream.Address))
            {
                failures.Add($"{upstreamPrefix}:Address is required.");
            }
            else if (endpointAddressPolicy.IsAmbiguousUpstreamAddress(upstream.Address))
            {
                failures.Add($"{upstreamPrefix}:Address must be a host name or IP literal without scheme, path, whitespace, or embedded port.");
            }

            var upstreamScheme = string.IsNullOrWhiteSpace(upstream.Scheme) ? "http" : upstream.Scheme;
            if (!IsSupportedUpstreamScheme(upstreamScheme))
            {
                failures.Add($"{upstreamPrefix}:Scheme must be 'http' or 'https'.");
            }

            var upstreamProtocol = string.IsNullOrWhiteSpace(upstream.Protocol) ? RuntimeUpstreamProtocol.Http1 : upstream.Protocol;
            if (!IsSupportedUpstreamProtocol(upstreamProtocol))
            {
                failures.Add($"{upstreamPrefix}:Protocol must be 'http1', 'http2', or 'http3'.");
            }
            else if (string.Equals(upstreamProtocol, RuntimeUpstreamProtocol.Http2, StringComparison.OrdinalIgnoreCase) && !string.Equals(upstreamScheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{upstreamPrefix}:HTTP/2 upstreams require scheme 'https' with ALPN; h2c is not supported.");
            }
            else if (string.Equals(upstreamProtocol, RuntimeUpstreamProtocol.Http3, StringComparison.OrdinalIgnoreCase) && !string.Equals(upstreamScheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{upstreamPrefix}:HTTP/3 upstreams require scheme 'https' with QUIC and ALPN; h3c is not supported.");
            }

            if (upstream.Port is < 1 or > 65535)
            {
                failures.Add($"{upstreamPrefix}:Port must be between 1 and 65535.");
            }

            if (upstream.Weight is < 1 or > 100_000)
            {
                failures.Add($"{upstreamPrefix}:Weight must be between 1 and 100000.");
            }

            ValidateUpstreamTls(failures, upstreamPrefix, upstream.UpstreamTls, endpointAddressPolicy, upstreamScheme);
            ValidateCircuitBreaker(failures, upstreamPrefix, upstream.CircuitBreaker);
        }
    }

    private static void ValidatePathRewrite(List<string> failures, string routePrefix, ProxyPathRewriteOptions rewrite)
    {
        if (!string.IsNullOrWhiteSpace(rewrite.StripPrefix) && !rewrite.StripPrefix.StartsWith('/'))
        {
            failures.Add($"{routePrefix}:PathRewrite:StripPrefix must start with '/'.");
        }

        if (!string.IsNullOrWhiteSpace(rewrite.ReplacePrefix) && !rewrite.ReplacePrefix.StartsWith('/'))
        {
            failures.Add($"{routePrefix}:PathRewrite:ReplacePrefix must start with '/'.");
        }

        if (!string.IsNullOrWhiteSpace(rewrite.Replacement) && !rewrite.Replacement.StartsWith('/'))
        {
            failures.Add($"{routePrefix}:PathRewrite:Replacement must start with '/'.");
        }

        if (!string.IsNullOrWhiteSpace(rewrite.StripPrefix) && !string.IsNullOrWhiteSpace(rewrite.ReplacePrefix))
        {
            failures.Add($"{routePrefix}:PathRewrite must not configure both StripPrefix and ReplacePrefix.");
        }

        if (!string.IsNullOrWhiteSpace(rewrite.ReplacePrefix) && string.IsNullOrWhiteSpace(rewrite.Replacement))
        {
            failures.Add($"{routePrefix}:PathRewrite:Replacement is required when ReplacePrefix is configured.");
        }
    }

    private static void ValidateRetryPolicy(List<string> failures, string routePrefix, ProxyRetryPolicyOptions retry, string routeAction)
    {
        if (retry.MaxAttempts is < 1 or > 5)
        {
            failures.Add($"{routePrefix}:Retry:MaxAttempts must be between 1 and 5.");
        }

        if (retry.PerAttemptTimeoutMs is < 0 or > 10 * 60 * 1000)
        {
            failures.Add($"{routePrefix}:Retry:PerAttemptTimeoutMs must be between 0 and 600000 milliseconds when configured.");
        }

        if (retry.RetryBackoffMilliseconds is < 0 or > 60_000)
        {
            failures.Add($"{routePrefix}:Retry:RetryBackoffMilliseconds must be between 0 and 60000.");
        }

        if (!retry.Enabled)
        {
            return;
        }

        if (!IsProxyAction(routeAction))
        {
            failures.Add($"{routePrefix}:Retry can only be enabled for proxy routes.");
        }

        if (retry.MaxAttempts < 2)
        {
            failures.Add($"{routePrefix}:Retry:MaxAttempts must be at least 2 when retry is enabled.");
        }

        if (retry.RetryMethods.Count == 0)
        {
            failures.Add($"{routePrefix}:Retry:RetryMethods must contain GET, HEAD, or both.");
        }

        for (var index = 0; index < retry.RetryMethods.Count; index++)
        {
            var method = retry.RetryMethods[index];
            if (!ProxyRequestMethodPolicy.IsSafeReadMethod(method))
            {
                failures.Add($"{routePrefix}:Retry:RetryMethods:{index} must be GET or HEAD.");
            }
        }

        for (var index = 0; index < retry.RetryOnStatusCodes.Count; index++)
        {
            var statusCode = retry.RetryOnStatusCodes[index];
            if (statusCode is < 500 or > 599)
            {
                failures.Add($"{routePrefix}:Retry:RetryOnStatusCodes:{index} must be a 5xx HTTP response status code.");
            }
        }
    }

    private static bool IsRouteAction(string action)
    {
        return IsProxyAction(action) || IsRedirectAction(action) || IsStaticResponseAction(action);
    }

    private static bool IsProxyAction(string action)
    {
        return string.Equals(action, "proxy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedirectAction(string action)
    {
        return string.Equals(action, "redirect", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStaticResponseAction(string action)
    {
        return string.Equals(action, "staticResponse", StringComparison.OrdinalIgnoreCase);
    }

    private static string SupportedListenerProtocolsText()
    {
        return string.Join(", ", RuntimeListenerProtocolExtensions.SupportedConfigValues.Select(static value => $"'{value}'"));
    }

    private static string SupportedHttp3EnablementsText()
    {
        return string.Join(", ", RuntimeHttp3Compatibility.SupportedEnablementConfigValues.Select(static value => $"'{value}'"));
    }

    private static bool IsSupportedUpstreamScheme(string scheme)
    {
        return string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedUpstreamProtocol(string protocol)
    {
        return string.Equals(protocol, RuntimeUpstreamProtocol.Http1, StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, RuntimeUpstreamProtocol.Http2, StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, RuntimeUpstreamProtocol.Http3, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateUpstreamTls(List<string> failures, string upstreamPrefix, UpstreamTlsOptions tls, IProxyEndpointAddressPolicy endpointAddressPolicy, string upstreamScheme)
    {
        if (tls.TrustedRoot is { } root)
        {
            if (!string.Equals(upstreamScheme, "https", StringComparison.OrdinalIgnoreCase))
                failures.Add($"{upstreamPrefix}:UpstreamTls:TrustedRoot requires scheme 'https'.");
            try
            {
                _ = new RuntimeTrustedRootCertificate(root.CertificatePath, root.Sha256);
            }
            catch (ArgumentException)
            {
                failures.Add($"{upstreamPrefix}:UpstreamTls:TrustedRoot requires a bounded absolute certificate path and 64 canonical uppercase SHA256 characters.");
            }
            if (!tls.ValidateCertificate)
            {
                failures.Add($"{upstreamPrefix}:UpstreamTls:TrustedRoot requires certificate validation.");
            }
        }

        if (!string.IsNullOrWhiteSpace(tls.SniHost) && !endpointAddressPolicy.IsValidSniHost(tls.SniHost))
        {
            failures.Add($"{upstreamPrefix}:UpstreamTls:SniHost must be a DNS host name or IP literal without scheme, path, port, whitespace, or wildcard.");
        }
    }

    private static void ValidateHealthCheck(List<string> failures, string routePrefix, HealthCheckOptions healthCheck)
    {
        var prefix = $"{routePrefix}:HealthCheck";
        if (string.IsNullOrWhiteSpace(healthCheck.Path) || !healthCheck.Path.StartsWith('/'))
        {
            failures.Add($"{prefix}:Path must start with '/'.");
        }

        if (healthCheck.IntervalSeconds is < 1 or > 3600)
        {
            failures.Add($"{prefix}:IntervalSeconds must be between 1 and 3600.");
        }

        if (healthCheck.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add($"{prefix}:TimeoutSeconds must be between 1 and 300.");
        }

        if (healthCheck.TimeoutSeconds > healthCheck.IntervalSeconds)
        {
            failures.Add($"{prefix}:TimeoutSeconds must not exceed IntervalSeconds.");
        }

        if (healthCheck.HealthyThreshold is < 1 or > 100)
        {
            failures.Add($"{prefix}:HealthyThreshold must be between 1 and 100.");
        }

        if (healthCheck.UnhealthyThreshold is < 1 or > 100)
        {
            failures.Add($"{prefix}:UnhealthyThreshold must be between 1 and 100.");
        }
    }

    private static void ValidateCircuitBreaker(List<string> failures, string upstreamPrefix, ProxyCircuitBreakerOptions circuitBreaker)
    {
        if (circuitBreaker.FailureThreshold is < 1 or > 1000)
        {
            failures.Add($"{upstreamPrefix}:CircuitBreaker:FailureThreshold must be between 1 and 1000.");
        }

        if (circuitBreaker.SamplingWindowSeconds is < 1 or > 3600)
        {
            failures.Add($"{upstreamPrefix}:CircuitBreaker:SamplingWindowSeconds must be between 1 and 3600.");
        }

        if (circuitBreaker.OpenDurationSeconds is < 1 or > 3600)
        {
            failures.Add($"{upstreamPrefix}:CircuitBreaker:OpenDurationSeconds must be between 1 and 3600.");
        }

        if (circuitBreaker.HalfOpenMaxAttempts is < 1 or > 100)
        {
            failures.Add($"{upstreamPrefix}:CircuitBreaker:HalfOpenMaxAttempts must be between 1 and 100.");
        }

        for (var index = 0; index < circuitBreaker.FailureStatusCodes.Count; index++)
        {
            var statusCode = circuitBreaker.FailureStatusCodes[index];
            if (statusCode is < 500 or > 599)
            {
                failures.Add($"{upstreamPrefix}:CircuitBreaker:FailureStatusCodes:{index} must be a 5xx HTTP response status code.");
            }
        }
    }

    private static void ValidateOverrides(List<string> failures, string routePrefix, ProxyRouteOverrideOptions overrides)
    {
        if (overrides.MaxRequestBodyBytes is < 0 or > 1L * 1024 * 1024 * 1024 * 1024)
        {
            failures.Add($"{routePrefix}:Overrides:MaxRequestBodyBytes must be between 0 and 1099511627776.");
        }

        if (overrides.ClientRequestHeadTimeoutMs.HasValue)
        {
            ValidateOverrideTimeout(failures, $"{routePrefix}:Overrides:ClientRequestHeadTimeoutMs", overrides.ClientRequestHeadTimeoutMs.Value);
        }

        if (overrides.UpstreamResponseHeadTimeoutMs.HasValue)
        {
            ValidateOverrideTimeout(failures, $"{routePrefix}:Overrides:UpstreamResponseHeadTimeoutMs", overrides.UpstreamResponseHeadTimeoutMs.Value);
        }

        if (overrides.UpstreamConnectTimeoutMs is { } connect)
            ValidateOverrideTimeout(failures, $"{routePrefix}:Overrides:UpstreamConnectTimeoutMs", connect);
        ValidateFlowTimeoutOverride(failures, $"{routePrefix}:Overrides:ClientRequestBodyIdleTimeoutMs", overrides.ClientRequestBodyIdleTimeoutMs);
        ValidateFlowTimeoutOverride(failures, $"{routePrefix}:Overrides:UpstreamResponseBodyIdleTimeoutMs", overrides.UpstreamResponseBodyIdleTimeoutMs);
        ValidateFlowTimeoutOverride(failures, $"{routePrefix}:Overrides:DownstreamWriteTimeoutMs", overrides.DownstreamWriteTimeoutMs);
    }

    private static void ValidateFlowTimeoutOverride(List<string> failures, string name, int? value)
    {
        if (value is < 100 or > 24 * 60 * 60 * 1000)
            failures.Add($"{name} must be between 100 and 86400000 milliseconds.");
    }

    private static void ValidateOverrideTimeout(List<string> failures, string name, int value)
    {
        if (value is < 100 or > 10 * 60 * 1000)
        {
            failures.Add($"{name} must be between 100 and 600000 milliseconds.");
        }
    }

    private static readonly HashSet<int> RedirectStatusCodes = [301, 302, 303, 307, 308];
    private static void ValidateRedirectPolicy(List<string> failures, string routePrefix, ProxyHttpsRedirectOptions redirect)
    {
        if (redirect.StatusCode.HasValue && !RedirectStatusCodes.Contains(redirect.StatusCode.Value))
        {
            failures.Add($"{routePrefix}:HttpsRedirect:StatusCode must be one of 301, 302, 303, 307, or 308.");
        }

        if (redirect.HttpsPort is < 1 or > 65535)
        {
            failures.Add($"{routePrefix}:HttpsRedirect:HttpsPort must be between 1 and 65535.");
        }
    }

    private static void ValidateCanonicalHost(List<string> failures, string routePrefix, ProxyCanonicalHostOptions canonicalHost)
    {
        if (canonicalHost.StatusCode.HasValue && !RedirectStatusCodes.Contains(canonicalHost.StatusCode.Value))
        {
            failures.Add($"{routePrefix}:CanonicalHost:StatusCode must be one of 301, 302, 303, 307, or 308.");
        }

        var enabled = canonicalHost.Enabled == true || !string.IsNullOrWhiteSpace(canonicalHost.TargetHost);
        if (!enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(canonicalHost.TargetHost))
        {
            failures.Add($"{routePrefix}:CanonicalHost:TargetHost is required when canonical host redirect is enabled.");
            return;
        }

        if (canonicalHost.TargetHost.Contains('/', StringComparison.Ordinal) || canonicalHost.TargetHost.Contains('\\', StringComparison.Ordinal) || canonicalHost.TargetHost.Any(char.IsWhiteSpace))
        {
            failures.Add($"{routePrefix}:CanonicalHost:TargetHost must be a host name, not a URL or path.");
        }
    }

    private static void ValidateRedirectRoute(List<string> failures, string routePrefix, ProxyRedirectOptions redirect, IProxyUrlSyntaxPolicy urlSyntaxPolicy)
    {
        var statusCode = redirect.StatusCode ?? 308;
        if (!RedirectStatusCodes.Contains(statusCode))
        {
            failures.Add($"{routePrefix}:Redirect:StatusCode must be one of 301, 302, 303, 307, or 308.");
        }

        var hasTargetUrl = !string.IsNullOrWhiteSpace(redirect.TargetUrl);
        var hasTargetPath = !string.IsNullOrWhiteSpace(redirect.TargetPath);
        if (hasTargetUrl == hasTargetPath)
        {
            failures.Add($"{routePrefix}:Redirect must set exactly one of TargetUrl or TargetPath.");
            return;
        }

        if (hasTargetUrl && !urlSyntaxPolicy.IsAbsoluteUrl(redirect.TargetUrl))
        {
            failures.Add($"{routePrefix}:Redirect:TargetUrl must be an absolute URL.");
        }

        if (hasTargetPath && !redirect.TargetPath.StartsWith('/'))
        {
            failures.Add($"{routePrefix}:Redirect:TargetPath must start with '/'.");
        }
    }

    public static IReadOnlyList<string> Validate(ProxyOptions options, IProxyEndpointAddressPolicy endpointAddressPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy)
    {
        ArgumentNullException.ThrowIfNull(endpointAddressPolicy);
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        ValidateListeners(failures, options, endpointAddressPolicy);
        ValidateRoutes(failures, options, endpointAddressPolicy, urlSyntaxPolicy);
        return failures;
    }

    private const long MaximumCacheEntryBytes = 64L * 1024 * 1024;
    private const long MaximumCacheTotalBytes = 512L * 1024 * 1024;
    private static void ValidateCachePolicy(List<string> failures, string routePrefix, ProxyCachePolicyOptions cache, string routeAction)
    {
        if (cache.MaxEntryBytes < 0)
        {
            failures.Add($"{routePrefix}:Cache:MaxEntryBytes must not be negative.");
        }

        if (cache.MaxTotalBytes < 0)
        {
            failures.Add($"{routePrefix}:Cache:MaxTotalBytes must not be negative.");
        }

        if (cache.DefaultTtlSeconds < 0)
        {
            failures.Add($"{routePrefix}:Cache:DefaultTtlSeconds must not be negative.");
        }

        if (!cache.Enabled)
        {
            return;
        }

        if (!IsProxyAction(routeAction))
        {
            failures.Add($"{routePrefix}:Cache can only be enabled for proxy routes.");
        }

        if (cache.MaxEntryBytes is <= 0 or > MaximumCacheEntryBytes)
        {
            failures.Add($"{routePrefix}:Cache:MaxEntryBytes must be between 1 and {MaximumCacheEntryBytes}.");
        }

        if (cache.MaxTotalBytes is <= 0 or > MaximumCacheTotalBytes)
        {
            failures.Add($"{routePrefix}:Cache:MaxTotalBytes must be between 1 and {MaximumCacheTotalBytes}.");
        }

        if (cache.MaxEntryBytes > 0 && cache.MaxTotalBytes > 0 && cache.MaxEntryBytes > cache.MaxTotalBytes)
        {
            failures.Add($"{routePrefix}:Cache:MaxEntryBytes must not exceed Cache:MaxTotalBytes.");
        }

        if (cache.DefaultTtlSeconds <= 0)
        {
            failures.Add($"{routePrefix}:Cache:DefaultTtlSeconds must be greater than 0.");
        }

        for (var index = 0; index < cache.VaryByHeaders.Count; index++)
        {
            var headerName = cache.VaryByHeaders[index];
            if (string.IsNullOrWhiteSpace(headerName) || !ProxyHeaderPolicyOptionsValidationRules.IsValidHttpFieldName(headerName))
            {
                failures.Add($"{routePrefix}:Cache:VaryByHeaders:{index} '{headerName}' is not a valid HTTP field name.");
            }
        }

        if (cache.CacheableStatusCodes.Count == 0)
        {
            failures.Add($"{routePrefix}:Cache:CacheableStatusCodes must contain at least one status code.");
        }

        for (var index = 0; index < cache.CacheableStatusCodes.Count; index++)
        {
            var statusCode = cache.CacheableStatusCodes[index];
            if (statusCode is < 200 or > 599)
            {
                failures.Add($"{routePrefix}:Cache:CacheableStatusCodes:{index} must be an HTTP response status code.");
            }
        }

        if (cache.Methods.Count == 0)
        {
            failures.Add($"{routePrefix}:Cache:Methods must contain GET, HEAD, or both.");
        }

        for (var index = 0; index < cache.Methods.Count; index++)
        {
            var method = cache.Methods[index];
            if (!ProxyRequestMethodPolicy.IsSafeReadMethod(method))
            {
                failures.Add($"{routePrefix}:Cache:Methods:{index} must be GET or HEAD.");
            }
        }
    }

    private const int MaxGeneratedBodyBytes = 64 * 1024;
    private static void ValidateStaticResponse(List<string> failures, string routePrefix, ProxyStaticResponseOptions response)
    {
        if (response.StatusCode is < 200 or > 599)
        {
            failures.Add($"{routePrefix}:StaticResponse:StatusCode must be between 200 and 599.");
        }

        if (string.IsNullOrWhiteSpace(response.ContentType) || response.ContentType.Any(static character => character is '\r' or '\n'))
        {
            failures.Add($"{routePrefix}:StaticResponse:ContentType must be a non-empty single-line value.");
        }

        if (Encoding.UTF8.GetByteCount(response.Body) > MaxGeneratedBodyBytes)
        {
            failures.Add($"{routePrefix}:StaticResponse:Body must not exceed {MaxGeneratedBodyBytes} UTF-8 bytes.");
        }
    }

    private static void ValidateMaintenance(List<string> failures, string routePrefix, ProxyMaintenanceOptions maintenance)
    {
        if (maintenance.RetryAfterSeconds is < 0 or > 86400)
        {
            failures.Add($"{routePrefix}:Maintenance:RetryAfterSeconds must be between 0 and 86400.");
        }

        if (string.IsNullOrWhiteSpace(maintenance.ContentType) || maintenance.ContentType.Any(static character => character is '\r' or '\n'))
        {
            failures.Add($"{routePrefix}:Maintenance:ContentType must be a non-empty single-line value.");
        }

        if (Encoding.UTF8.GetByteCount(maintenance.Body) > MaxGeneratedBodyBytes)
        {
            failures.Add($"{routePrefix}:Maintenance:Body must not exceed {MaxGeneratedBodyBytes} UTF-8 bytes.");
        }
    }

    private static void ValidateListeners(List<string> failures, ProxyOptions options, IProxyEndpointAddressPolicy endpointAddressPolicy)
    {
        if (options.Listeners.Count == 0)
        {
            failures.Add("Proxy:Listeners must contain at least one listener.");
        }

        HashSet<string> listenerNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> listenerBinds = new(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < options.Listeners.Count; index++)
        {
            var listener = options.Listeners[index];
            var prefix = $"Proxy:Listeners:{index}";
            if (string.IsNullOrWhiteSpace(listener.Name))
            {
                failures.Add($"{prefix}:Name is required.");
            }
            else if (!listenerNames.Add(listener.Name))
            {
                failures.Add($"{prefix}:Name '{listener.Name}' is duplicated.");
            }

            if (!endpointAddressPolicy.IsListenerAddress(listener.Address))
            {
                failures.Add($"{prefix}:Address must be an IP address for Phase 1.");
            }

            var isHttp = string.Equals(listener.Transport, "http", StringComparison.OrdinalIgnoreCase);
            var isHttps = string.Equals(listener.Transport, "https", StringComparison.OrdinalIgnoreCase);
            if (!isHttp && !isHttps)
            {
                failures.Add($"{prefix}:Transport must be 'http' or 'https'.");
            }

            var http3Compatibility = RuntimeHttp3Compatibility.From(listener);
            var listenerProtocols = http3Compatibility.Protocols;
            if (!http3Compatibility.ProtocolsValid)
            {
                failures.Add($"{prefix}:Protocols must be {SupportedListenerProtocolsText()}.");
            }
            else if (listenerProtocols.HasFlag(RuntimeListenerProtocols.Http2) && !isHttps)
            {
                failures.Add($"{prefix}:HTTP/2 requires an HTTPS listener with ALPN; h2c is not supported.");
            }

            var http3Enablement = http3Compatibility.EffectiveEnablement;
            var explicitHttp3Requested = http3Compatibility.ExplicitHttp3Requested;
            if (explicitHttp3Requested)
            {
                if (http3Enablement == RuntimeHttp3Enablement.Disabled)
                {
                    failures.Add($"{prefix}:HTTP/3 protocols cannot be combined with Http3Enablement 'disabled'.");
                }

                if (!isHttps)
                {
                    failures.Add($"{prefix}:HTTP/3 requires an HTTPS listener; QUIC TLS over plaintext is not supported.");
                }

                if (string.IsNullOrWhiteSpace(listener.DefaultCertificateId) && listener.SniCertificates.Count == 0)
                {
                    failures.Add($"{prefix}:HTTP/3 requires DefaultCertificateId or SniCertificates so QUIC TLS can use the certificate registry.");
                }
            }

            if (!http3Compatibility.EnablementValid)
            {
                failures.Add($"{prefix}:Http3Enablement must be {SupportedHttp3EnablementsText()} when configured.");
            }

            if (listener.Http3AltSvcMaxAgeSeconds is < 0 or > 31536000)
            {
                failures.Add($"{prefix}:Http3AltSvcMaxAgeSeconds must be between 0 and 31536000.");
            }

            if (listener.Http3AltSvcEnabled && string.Equals(listener.Http3Enablement, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{prefix}:Http3AltSvcEnabled cannot be true when Http3Enablement is 'disabled'.");
            }

            if (listener.Port is < 1 or > 65535)
            {
                failures.Add($"{prefix}:Port must be between 1 and 65535.");
            }

            if (listener.Backlog < 1)
            {
                failures.Add($"{prefix}:Backlog must be greater than zero.");
            }

            if (listener.MaxRequestHeadBytes is < 1024 or > 1024 * 1024)
            {
                failures.Add($"{prefix}:MaxRequestHeadBytes must be between 1024 and 1048576.");
            }

            if (listener.ForwardingBufferBytes is < 4096 or > 1024 * 1024)
            {
                failures.Add($"{prefix}:ForwardingBufferBytes must be between 4096 and 1048576.");
            }

            if (listener.MaxResponseHeadBytes is < 1024 or > 1024 * 1024)
            {
                failures.Add($"{prefix}:MaxResponseHeadBytes must be between 1024 and 1048576.");
            }

            if (listener.MaxChunkLineBytes is < 64 or > 16 * 1024)
            {
                failures.Add($"{prefix}:MaxChunkLineBytes must be between 64 and 16384.");
            }

            if (listener.Http2MaxConcurrentStreams is < 1 or > 1000)
            {
                failures.Add($"{prefix}:Http2MaxConcurrentStreams must be between 1 and 1000.");
            }

            if (listener.Http2MaxHeaderListBytes is < 1024 or > 1024 * 1024)
            {
                failures.Add($"{prefix}:Http2MaxHeaderListBytes must be between 1024 and 1048576.");
            }

            if (listener.Http2MaxFrameSize is < 16 * 1024 or > 16 * 1024 * 1024 - 1)
            {
                failures.Add($"{prefix}:Http2MaxFrameSize must be between 16384 and 16777215.");
            }

            var bindKey = $"{listener.Address.Trim().ToLowerInvariant()}|{listener.Port}|{listener.Transport.Trim().ToLowerInvariant()}";
            if (listener.Enabled && !listenerBinds.Add(bindKey))
            {
                failures.Add($"{prefix}:Listener bind {listener.Address}:{listener.Port}/{listener.Transport} is duplicated.");
            }
        }

        if (options.Listeners.Count > 0 && !options.Listeners.Any(static listener => listener.Enabled))
        {
            failures.Add("Proxy:Listeners must contain at least one enabled listener.");
        }
    }
}
