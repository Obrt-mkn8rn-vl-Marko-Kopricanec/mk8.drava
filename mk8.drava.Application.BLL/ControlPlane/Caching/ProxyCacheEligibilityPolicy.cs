using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public static partial class ProxyCacheEligibilityPolicy
{
    public const string ReasonAuthorization = "authorization";
    public const string ReasonCacheControlMustRevalidate = "cache-control-must-revalidate";
    public const string ReasonCacheControlNoCache = "cache-control-no-cache";
    public const string ReasonCacheControlNoStore = "cache-control-no-store";
    public const string ReasonCacheControlPrivate = "cache-control-private";
    public const string ReasonCookie = "cookie";
    public const string ReasonDisabled = "disabled";
    public const string ReasonFraming = "framing";
    public const string ReasonMethod = "method";
    public const string ReasonOversized = "oversized";
    public const string ReasonRequestBody = "request-body";
    public const string ReasonSetCookie = "set-cookie";
    public const string ReasonStatus = "status";
    public const string ReasonTtl = "ttl";
    public static ProxyCacheEligibilityResult EvaluateRequest(ProxyCachePolicyFacts policy, Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.Enabled)
        {
            return ProxyCacheEligibilityResult.Rejected(ReasonDisabled);
        }

        if (!ContainsMethod(policy.Methods, requestHead.Method))
        {
            return ProxyCacheEligibilityResult.Rejected(ReasonMethod);
        }

        if (requestHead.Framing.Kind != Http1BodyKind.None)
        {
            return ProxyCacheEligibilityResult.Rejected(ReasonRequestBody);
        }

        if (ContainsHeader(requestHead.Headers, "Authorization"))
        {
            return ProxyCacheEligibilityResult.Rejected(ReasonAuthorization);
        }

        if (ContainsHeader(requestHead.Headers, "Cookie"))
        {
            return ProxyCacheEligibilityResult.Rejected(ReasonCookie);
        }

        return ProxyCacheEligibilityResult.Accepted();
    }

    private static bool ContainsHeader(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        return headers.Any(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsStatus(IReadOnlyList<int> statusCodes, int statusCode)
    {
        return statusCodes.Any(code => code == statusCode);
    }

    private static bool ContainsMethod(IReadOnlyList<string> methods, string method)
    {
        return methods.Any(value => string.Equals(value, method, StringComparison.OrdinalIgnoreCase));
    }

    public static ProxyCacheEligibilityResult EvaluateResponseForBuffering(ProxyCachePolicyFacts policy, Http1RequestHead requestHead, Http1ResponseHead responseHead)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(responseHead);
        var requestEligibility = EvaluateRequest(policy, requestHead);
        if (requestEligibility is ProxyCacheEligibilityResult.RejectedResult)
        {
            return requestEligibility;
        }

        var metadataEligibility = EvaluateResponseMetadata(policy, responseHead);
        if (metadataEligibility is CacheResponseMetadataEligibility.Rejected rejectedMetadata)
        {
            return ProxyCacheEligibilityResult.Rejected(rejectedMetadata.Reason);
        }

        var framingEligibility = EvaluateResponseFraming(policy, responseHead);
        if (framingEligibility is ProxyCacheResponseFramingEligibility.Rejected rejectedFraming)
        {
            return ProxyCacheEligibilityResult.Rejected(rejectedFraming.Reason);
        }

        return ProxyCacheEligibilityResult.Accepted();
    }

    public static ProxyCacheStorageEligibilityResult EvaluateStoredResponse(ProxyCachePolicyFacts policy, Http1ResponseHead responseHead, long bodyLength)
    {
        ArgumentNullException.ThrowIfNull(responseHead);
        ArgumentNullException.ThrowIfNull(policy);
        var metadataEligibility = EvaluateResponseMetadata(policy, responseHead);
        if (metadataEligibility is not CacheResponseMetadataEligibility.Accepted acceptedMetadata)
        {
            return ProxyCacheStorageEligibilityResult.Rejected(((CacheResponseMetadataEligibility.Rejected)metadataEligibility).Reason);
        }

        if (bodyLength > policy.MaxEntryBytes)
        {
            return ProxyCacheStorageEligibilityResult.Rejected(ReasonOversized);
        }

        return ProxyCacheStorageEligibilityResult.Accepted(acceptedMetadata.Ttl);
    }

    public static ProxyCacheResponseFramingEligibility EvaluateResponseFraming(ProxyCachePolicyFacts policy, Http1ResponseHead responseHead)
    {
        ArgumentNullException.ThrowIfNull(responseHead);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.Enabled)
        {
            return ProxyCacheResponseFramingEligibility.Accept();
        }

        if (responseHead.Framing.Kind is Http1BodyKind.Chunked or Http1BodyKind.CloseDelimited)
        {
            return ProxyCacheResponseFramingEligibility.Reject(ReasonFraming);
        }

        if (responseHead.Framing.Kind == Http1BodyKind.ContentLength && responseHead.Framing.ContentLength.GetValueOrDefault() > policy.MaxEntryBytes)
        {
            return ProxyCacheResponseFramingEligibility.Reject(ReasonOversized);
        }

        return ProxyCacheResponseFramingEligibility.Accept();
    }

    private static CacheResponseMetadataEligibility EvaluateResponseMetadata(ProxyCachePolicyFacts policy, Http1ResponseHead responseHead)
    {
        if (!ContainsStatus(policy.CacheableStatusCodes, responseHead.StatusCode))
        {
            return CacheResponseMetadataEligibility.Reject(ReasonStatus);
        }

        if (ContainsHeader(responseHead.Headers, "Set-Cookie"))
        {
            return CacheResponseMetadataEligibility.Reject(ReasonSetCookie);
        }

        var ttlResolution = ResolveTtl(policy, responseHead.Headers);
        if (ttlResolution is not CacheTtlResolution.Resolved resolvedTtl)
        {
            return CacheResponseMetadataEligibility.Reject(((CacheTtlResolution.Rejected)ttlResolution).Reason);
        }

        return CacheResponseMetadataEligibility.Accept(resolvedTtl.Ttl);
    }

    private static CacheTtlResolution ResolveTtl(ProxyCachePolicyFacts policy, IReadOnlyList<ProxyHeaderField> headers)
    {
        var ttl = policy.DefaultTtl;
        if (!policy.RespectOriginCacheControl)
        {
            return ttl > TimeSpan.Zero ? CacheTtlResolution.Resolve(ttl) : CacheTtlResolution.Reject(ReasonTtl);
        }

        var directives = CacheControlDirectives(headers);
        if (directives.ContainsKey("no-store"))
        {
            return CacheTtlResolution.Reject(ReasonCacheControlNoStore);
        }

        if (directives.ContainsKey("private"))
        {
            return CacheTtlResolution.Reject(ReasonCacheControlPrivate);
        }

        if (directives.ContainsKey("no-cache"))
        {
            return CacheTtlResolution.Reject(ReasonCacheControlNoCache);
        }

        if (directives.ContainsKey("must-revalidate"))
        {
            return CacheTtlResolution.Reject(ReasonCacheControlMustRevalidate);
        }

        if (directives.TryGetValue("max-age", out var maxAgeValue) && int.TryParse(maxAgeValue, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var maxAgeSeconds))
        {
            ttl = TimeSpan.FromSeconds(maxAgeSeconds);
        }

        if (ttl <= TimeSpan.Zero)
        {
            return CacheTtlResolution.Reject(ReasonTtl);
        }

        return CacheTtlResolution.Resolve(ttl);
    }

    private static Dictionary<string, string?> CacheControlDirectives(IReadOnlyList<ProxyHeaderField> headers)
    {
        Dictionary<string, string?> directives = new(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (!string.Equals(header.Name, "Cache-Control", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var part in header.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = part.IndexOf('=');
                if (equals < 0)
                {
                    directives[part] = null;
                    continue;
                }

                directives[part[..equals].Trim()] = part[(equals + 1)..].Trim().Trim('"');
            }
        }

        return directives;
    }

    private abstract record CacheTtlResolution
    {
        private CacheTtlResolution()
        {
        }

        public static CacheTtlResolution Resolve(TimeSpan ttl)
        {
            return new Resolved(ttl);
        }

        public static CacheTtlResolution Reject(string reason)
        {
            return new Rejected(reason);
        }

        public sealed record Resolved : CacheTtlResolution
        {
            public Resolved(TimeSpan ttl)
            {
                if (ttl <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(ttl), "Cache TTL must be positive.");
                }

                Ttl = ttl;
            }

            public TimeSpan Ttl { get; }
        }

        public sealed record Rejected : CacheTtlResolution
        {
            public Rejected(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("Cache TTL rejection reason is required.", nameof(reason));
                }

                Reason = reason;
            }

            public string Reason { get; }
        }
    }

    private abstract record CacheResponseMetadataEligibility
    {
        private CacheResponseMetadataEligibility()
        {
        }

        public static CacheResponseMetadataEligibility Accept(TimeSpan ttl)
        {
            return new Accepted(ttl);
        }

        public static CacheResponseMetadataEligibility Reject(string reason)
        {
            return new Rejected(reason);
        }

        public sealed record Accepted : CacheResponseMetadataEligibility
        {
            public Accepted(TimeSpan ttl)
            {
                if (ttl <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(ttl), "Cache metadata TTL must be positive.");
                }

                Ttl = ttl;
            }

            public TimeSpan Ttl { get; }
        }

        public sealed record Rejected : CacheResponseMetadataEligibility
        {
            public Rejected(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("Cache metadata rejection reason is required.", nameof(reason));
                }

                Reason = reason;
            }

            public string Reason { get; }
        }
    }
}
