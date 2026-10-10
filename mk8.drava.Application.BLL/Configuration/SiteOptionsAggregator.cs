namespace Mk8.Drava.Application.BLL.Configuration;
public static partial class SiteOptionsAggregator
{
    private static ProxyHttpsRedirectOptions MergeHttpsRedirect(ProxyHttpsRedirectOptions site, ProxyHttpsRedirectOptions route)
    {
        return new ProxyHttpsRedirectOptions
        {
            Enabled = route.Enabled ?? site.Enabled,
            StatusCode = route.StatusCode ?? site.StatusCode,
            HttpsPort = route.HttpsPort ?? site.HttpsPort
        };
    }

    private static ProxyCanonicalHostOptions MergeCanonicalHost(ProxyCanonicalHostOptions site, ProxyCanonicalHostOptions route)
    {
        return new ProxyCanonicalHostOptions
        {
            Enabled = route.Enabled ?? site.Enabled,
            TargetHost = string.IsNullOrWhiteSpace(route.TargetHost) ? site.TargetHost : route.TargetHost,
            StatusCode = route.StatusCode ?? site.StatusCode
        };
    }

    private static ProxyHeaderPolicyOptions MergeHeaderPolicy(ProxyHeaderPolicyOptions site, ProxyHeaderPolicyOptions route)
    {
        return new ProxyHeaderPolicyOptions
        {
            SetRequestHeaders = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.ProxyHeaderSetOptions>(CopyHeaderFields(site.SetRequestHeaders.Concat(route.SetRequestHeaders))),
            RemoveRequestHeaders = new System.Collections.ObjectModel.Collection<string>(site.RemoveRequestHeaders.Concat(route.RemoveRequestHeaders).ToList()),
            SetResponseHeaders = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.ProxyHeaderSetOptions>(CopyHeaderFields(site.SetResponseHeaders.Concat(route.SetResponseHeaders))),
            RemoveResponseHeaders = new System.Collections.ObjectModel.Collection<string>(site.RemoveResponseHeaders.Concat(route.RemoveResponseHeaders).ToList())
        };
    }

    private static ProxyMaintenanceOptions MergeMaintenance(ProxyMaintenanceOptions site, ProxyMaintenanceOptions route)
    {
        return new ProxyMaintenanceOptions
        {
            Enabled = route.Enabled ?? site.Enabled,
            RetryAfterSeconds = route.RetryAfterSeconds ?? site.RetryAfterSeconds,
            ContentType = string.IsNullOrWhiteSpace(route.ContentType) || string.Equals(route.ContentType, "text/plain; charset=utf-8", StringComparison.OrdinalIgnoreCase) ? site.ContentType : route.ContentType,
            Body = string.Equals(route.Body, "Service Unavailable", StringComparison.Ordinal) ? site.Body : route.Body
        };
    }

    private static ProxyCachePolicyOptions MergeCache(ProxyCachePolicyOptions site, ProxyCachePolicyOptions route)
    {
        return route.Enabled ? CopyCache(route) : CopyCache(site);
    }

    private static ProxyRetryPolicyOptions MergeRetry(ProxyRetryPolicyOptions site, ProxyRetryPolicyOptions route)
    {
        return route.Enabled ? CopyRetry(route) : CopyRetry(site);
    }

    private static ProxyRouteOverrideOptions MergeOverrides(ProxyRouteOverrideOptions site, ProxyRouteOverrideOptions route)
    {
        return new ProxyRouteOverrideOptions
        {
            MaxRequestBodyBytes = route.MaxRequestBodyBytes ?? site.MaxRequestBodyBytes,
            ClientRequestHeadTimeoutMs = route.ClientRequestHeadTimeoutMs ?? site.ClientRequestHeadTimeoutMs,
            UpstreamResponseHeadTimeoutMs = route.UpstreamResponseHeadTimeoutMs ?? site.UpstreamResponseHeadTimeoutMs,
            AccessLogEnabled = route.AccessLogEnabled ?? site.AccessLogEnabled
        };
    }

    public static ProxyOptions ToProxyOptions(IEnumerable<SiteConfigurationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Dictionary<string, ListenerOptions> listenersByKey = new(StringComparer.OrdinalIgnoreCase);
        List<ProxyRouteOptions> routes = [];
        foreach (var source in sources)
        {
            foreach (var listener in source.Site.Listeners)
            {
                var key = GetListenerKey(listener);
                if (listenersByKey.TryGetValue(key, out var existing))
                {
                    listenersByKey[key] = MergeListeners(existing, listener);
                }
                else
                {
                    listenersByKey.Add(key, CopyListener(listener));
                }
            }

            if (source.Site.Routes.Count == 0)
            {
                routes.Add(new ProxyRouteOptions { Name = source.Site.Name, SiteName = source.Site.Name, Host = source.Site.Host, PathPrefix = source.Site.PathPrefix, Action = "proxy", LoadBalancingPolicy = source.Site.LoadBalancingPolicy, HealthCheck = CopyHealthCheck(source.Site.HealthCheck), Upstreams = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.UpstreamOptions>(CopyUpstreams(source.Site.Upstreams)), HttpsRedirect = CopyHttpsRedirect(source.Site.HttpsRedirect), CanonicalHost = CopyCanonicalHost(source.Site.CanonicalHost), HeaderPolicy = CopyHeaderPolicy(source.Site.HeaderPolicy), Maintenance = CopyMaintenance(source.Site.Maintenance), Cache = CopyCache(source.Site.Cache), Retry = CopyRetry(source.Site.Retry), Overrides = CopyOverrides(source.Site.Overrides) });
                continue;
            }

            foreach (var route in source.Site.Routes)
            {
                routes.Add(new ProxyRouteOptions { Name = route.Name, SiteName = source.Site.Name, Host = string.IsNullOrWhiteSpace(route.Host) || string.Equals(route.Host, "*", StringComparison.Ordinal) ? source.Site.Host : route.Host, PathPrefix = route.PathPrefix, Action = route.Action, LoadBalancingPolicy = string.IsNullOrWhiteSpace(route.LoadBalancingPolicy) ? source.Site.LoadBalancingPolicy : route.LoadBalancingPolicy, HealthCheck = CopyHealthCheck(route.HealthCheck), Upstreams = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.UpstreamOptions>(CopyUpstreams(route.Upstreams.Count > 0 ? route.Upstreams : source.Site.Upstreams)), HttpsRedirect = MergeHttpsRedirect(source.Site.HttpsRedirect, route.HttpsRedirect), CanonicalHost = MergeCanonicalHost(source.Site.CanonicalHost, route.CanonicalHost), HeaderPolicy = MergeHeaderPolicy(source.Site.HeaderPolicy, route.HeaderPolicy), PathRewrite = CopyPathRewrite(route.PathRewrite), Redirect = CopyRedirect(route.Redirect), StaticResponse = CopyStaticResponse(route.StaticResponse), Maintenance = MergeMaintenance(source.Site.Maintenance, route.Maintenance), Cache = MergeCache(source.Site.Cache, route.Cache), Retry = MergeRetry(source.Site.Retry, route.Retry), Overrides = MergeOverrides(source.Site.Overrides, route.Overrides) });
            }
        }

        return new ProxyOptions
        {
            Listeners = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.ListenerOptions>(listenersByKey.Values.ToList()),
            Routes = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.ProxyRouteOptions>(routes)
        };
    }

    private static ListenerOptions CopyListener(ListenerOptions source)
    {
        return new ListenerOptions
        {
            Name = source.Name,
            Address = source.Address,
            Port = source.Port,
            Enabled = source.Enabled,
            Transport = source.Transport,
            Protocols = source.Protocols,
            Http3Enablement = source.Http3Enablement,
            Http3AltSvcEnabled = source.Http3AltSvcEnabled,
            Http3AltSvcMaxAgeSeconds = source.Http3AltSvcMaxAgeSeconds,
            DefaultCertificateId = source.DefaultCertificateId,
            SniCertificates = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.SniCertificateOptions>(CopySniCertificates(source.SniCertificates)),
            Backlog = source.Backlog,
            MaxRequestHeadBytes = source.MaxRequestHeadBytes,
            MaxResponseHeadBytes = source.MaxResponseHeadBytes,
            MaxChunkLineBytes = source.MaxChunkLineBytes,
            ForwardingBufferBytes = source.ForwardingBufferBytes,
            Http2MaxConcurrentStreams = source.Http2MaxConcurrentStreams,
            Http2MaxHeaderListBytes = source.Http2MaxHeaderListBytes,
            Http2MaxFrameSize = source.Http2MaxFrameSize
        };
    }

    private static List<SniCertificateOptions> CopySniCertificates(IEnumerable<SniCertificateOptions> source)
    {
        return source.Select(static binding => new SniCertificateOptions { HostName = binding.HostName, CertificateId = binding.CertificateId }).ToList();
    }

    private static List<UpstreamOptions> CopyUpstreams(IEnumerable<UpstreamOptions> source)
    {
        return source.Select(CopyUpstream).ToList();
    }

    private static UpstreamOptions CopyUpstream(UpstreamOptions source)
    {
        return new UpstreamOptions
        {
            Name = source.Name,
            Scheme = source.Scheme,
            Protocol = source.Protocol,
            Address = source.Address,
            Port = source.Port,
            Weight = source.Weight,
            UpstreamTls = new UpstreamTlsOptions
            {
                ValidateCertificate = source.UpstreamTls.ValidateCertificate,
                SniHost = source.UpstreamTls.SniHost,
                TrustedRoot = source.UpstreamTls.TrustedRoot is { } root ? new TrustedRootCertificateOptions
                {
                    CertificatePath = root.CertificatePath,
                    Sha256 = root.Sha256,
                } : null
            },
            CircuitBreaker = new ProxyCircuitBreakerOptions
            {
                Enabled = source.CircuitBreaker.Enabled,
                FailureThreshold = source.CircuitBreaker.FailureThreshold,
                SamplingWindowSeconds = source.CircuitBreaker.SamplingWindowSeconds,
                OpenDurationSeconds = source.CircuitBreaker.OpenDurationSeconds,
                HalfOpenMaxAttempts = source.CircuitBreaker.HalfOpenMaxAttempts,
                FailureStatusCodes = new System.Collections.ObjectModel.Collection<int>(source.CircuitBreaker.FailureStatusCodes.ToList())
            }
        };
    }

    private static HealthCheckOptions CopyHealthCheck(HealthCheckOptions source)
    {
        return new HealthCheckOptions
        {
            Enabled = source.Enabled,
            Path = source.Path,
            IntervalSeconds = source.IntervalSeconds,
            TimeoutSeconds = source.TimeoutSeconds,
            HealthyThreshold = source.HealthyThreshold,
            UnhealthyThreshold = source.UnhealthyThreshold
        };
    }

    private static ProxyHttpsRedirectOptions CopyHttpsRedirect(ProxyHttpsRedirectOptions source)
    {
        return new ProxyHttpsRedirectOptions
        {
            Enabled = source.Enabled,
            StatusCode = source.StatusCode,
            HttpsPort = source.HttpsPort
        };
    }

    private static ProxyCanonicalHostOptions CopyCanonicalHost(ProxyCanonicalHostOptions source)
    {
        return new ProxyCanonicalHostOptions
        {
            Enabled = source.Enabled,
            TargetHost = source.TargetHost,
            StatusCode = source.StatusCode
        };
    }

    private static ProxyHeaderPolicyOptions CopyHeaderPolicy(ProxyHeaderPolicyOptions source)
    {
        return new ProxyHeaderPolicyOptions
        {
            SetRequestHeaders = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.ProxyHeaderSetOptions>(CopyHeaderFields(source.SetRequestHeaders)),
            RemoveRequestHeaders = new System.Collections.ObjectModel.Collection<string>(source.RemoveRequestHeaders.ToList()),
            SetResponseHeaders = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.ProxyHeaderSetOptions>(CopyHeaderFields(source.SetResponseHeaders)),
            RemoveResponseHeaders = new System.Collections.ObjectModel.Collection<string>(source.RemoveResponseHeaders.ToList())
        };
    }

    private static List<ProxyHeaderSetOptions> CopyHeaderFields(IEnumerable<ProxyHeaderSetOptions> source)
    {
        return source.Select(static header => new ProxyHeaderSetOptions { Name = header.Name, Value = header.Value }).ToList();
    }

    private static ProxyPathRewriteOptions CopyPathRewrite(ProxyPathRewriteOptions source)
    {
        return new ProxyPathRewriteOptions
        {
            StripPrefix = source.StripPrefix,
            ReplacePrefix = source.ReplacePrefix,
            Replacement = source.Replacement
        };
    }

    private static ProxyRedirectOptions CopyRedirect(ProxyRedirectOptions source)
    {
        return new ProxyRedirectOptions
        {
            StatusCode = source.StatusCode,
            TargetUrl = source.TargetUrl,
            TargetPath = source.TargetPath,
            PreserveQuery = source.PreserveQuery
        };
    }

    private static ProxyStaticResponseOptions CopyStaticResponse(ProxyStaticResponseOptions source)
    {
        return new ProxyStaticResponseOptions
        {
            StatusCode = source.StatusCode,
            ContentType = source.ContentType,
            Body = source.Body
        };
    }

    private static ProxyMaintenanceOptions CopyMaintenance(ProxyMaintenanceOptions source)
    {
        return new ProxyMaintenanceOptions
        {
            Enabled = source.Enabled,
            RetryAfterSeconds = source.RetryAfterSeconds,
            ContentType = source.ContentType,
            Body = source.Body
        };
    }

    private static ProxyCachePolicyOptions CopyCache(ProxyCachePolicyOptions source)
    {
        return new ProxyCachePolicyOptions
        {
            Enabled = source.Enabled,
            MaxEntryBytes = source.MaxEntryBytes,
            MaxTotalBytes = source.MaxTotalBytes,
            DefaultTtlSeconds = source.DefaultTtlSeconds,
            RespectOriginCacheControl = source.RespectOriginCacheControl,
            VaryByHeaders = new System.Collections.ObjectModel.Collection<string>(source.VaryByHeaders.ToList()),
            CacheableStatusCodes = new System.Collections.ObjectModel.Collection<int>(source.CacheableStatusCodes.ToList()),
            Methods = new System.Collections.ObjectModel.Collection<string>(source.Methods.ToList())
        };
    }

    private static ProxyRetryPolicyOptions CopyRetry(ProxyRetryPolicyOptions source)
    {
        return new ProxyRetryPolicyOptions
        {
            Enabled = source.Enabled,
            MaxAttempts = source.MaxAttempts,
            PerAttemptTimeoutMs = source.PerAttemptTimeoutMs,
            RetryOnConnectFailure = source.RetryOnConnectFailure,
            RetryOnUpstreamResponseHeadTimeout = source.RetryOnUpstreamResponseHeadTimeout,
            RetryOnStatusCodes = new System.Collections.ObjectModel.Collection<int>(source.RetryOnStatusCodes.ToList()),
            RetryMethods = new System.Collections.ObjectModel.Collection<string>(source.RetryMethods.ToList()),
            RetryBackoffMilliseconds = source.RetryBackoffMilliseconds
        };
    }

    private static ProxyRouteOverrideOptions CopyOverrides(ProxyRouteOverrideOptions source)
    {
        return new ProxyRouteOverrideOptions
        {
            MaxRequestBodyBytes = source.MaxRequestBodyBytes,
            ClientRequestHeadTimeoutMs = source.ClientRequestHeadTimeoutMs,
            UpstreamResponseHeadTimeoutMs = source.UpstreamResponseHeadTimeoutMs,
            AccessLogEnabled = source.AccessLogEnabled
        };
    }

    private static string GetListenerKey(ListenerOptions listener)
    {
        return $"{listener.Name}|{listener.Address}|{listener.Port}|{listener.Transport}";
    }

    private static ListenerOptions MergeListeners(ListenerOptions existing, ListenerOptions next)
    {
        var sniCertificates = CopySniCertificates(existing.SniCertificates.Concat(next.SniCertificates));
        return new ListenerOptions
        {
            Name = existing.Name,
            Address = existing.Address,
            Port = existing.Port,
            Enabled = existing.Enabled || next.Enabled,
            Transport = existing.Transport,
            Protocols = MergeListenerProtocols(existing.Protocols, next.Protocols),
            Http3Enablement = MergeHttp3Enablement(existing.Http3Enablement, next.Http3Enablement),
            Http3AltSvcEnabled = existing.Http3AltSvcEnabled || next.Http3AltSvcEnabled,
            Http3AltSvcMaxAgeSeconds = existing.Http3AltSvcMaxAgeSeconds,
            DefaultCertificateId = !string.IsNullOrWhiteSpace(existing.DefaultCertificateId) ? existing.DefaultCertificateId : next.DefaultCertificateId,
            SniCertificates = new System.Collections.ObjectModel.Collection<Mk8.Drava.Application.BLL.Configuration.SniCertificateOptions>(sniCertificates),
            Backlog = existing.Backlog,
            MaxRequestHeadBytes = existing.MaxRequestHeadBytes,
            MaxResponseHeadBytes = existing.MaxResponseHeadBytes,
            MaxChunkLineBytes = existing.MaxChunkLineBytes,
            ForwardingBufferBytes = existing.ForwardingBufferBytes,
            Http2MaxConcurrentStreams = existing.Http2MaxConcurrentStreams,
            Http2MaxHeaderListBytes = existing.Http2MaxHeaderListBytes,
            Http2MaxFrameSize = existing.Http2MaxFrameSize
        };
    }

    private static string MergeHttp3Enablement(string existing, string next)
    {
        return RuntimeHttp3Compatibility.MergeEnablementConfigText(existing, next);
    }

    private static string MergeListenerProtocols(string existing, string next)
    {
        var existingParsing = RuntimeListenerProtocolExtensions.ParseConfigText(existing);
        if (existingParsing is not RuntimeListenerProtocolParseResult.AcceptedResult existingProtocols)
        {
            return existing;
        }

        var nextParsing = RuntimeListenerProtocolExtensions.ParseConfigText(next);
        if (nextParsing is not RuntimeListenerProtocolParseResult.AcceptedResult nextProtocols)
        {
            return next;
        }

        var merged = existingProtocols.Protocols | nextProtocols.Protocols;
        return merged.ToConfigText();
    }
}
