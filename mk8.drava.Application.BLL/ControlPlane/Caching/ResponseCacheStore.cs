using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using System.Text;

namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed partial class ResponseCacheStore : IProxyCacheControl
{
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _rejections = new(StringComparer.OrdinalIgnoreCase);
    private long _approximateBytes;
    private long _hits;
    private long _misses;
    private long _stores;
    private long _evictions;
    private long _storeRejections;
    private DateTimeOffset? _lastClearedAtUtc;
    private string? _lastClearReason;
    public ResponseCacheStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public ProxyCacheLookupResult Get(ProxyCacheRequestScope scope, Http1RequestHead requestHead, string upstreamTarget)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(upstreamTarget);
        if (CreateKey(scope, requestHead, upstreamTarget)is not CacheKeyCreation.Created createdKey)
        {
            return ProxyCacheLookupResult.Miss;
        }

        var key = createdKey.Key;
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                _misses++;
                return ProxyCacheLookupResult.Miss;
            }

            if (entry.ExpiresAtUtc <= now)
            {
                RemoveEntry(key, entry);
                _misses++;
                return ProxyCacheLookupResult.Miss;
            }

            entry.RecordAccess(now);
            _hits++;
            return ProxyCacheLookupResult.Hit(entry.ToResponse());
        }
    }

    public void Store(ProxyCacheRequestScope scope, Http1RequestHead requestHead, string upstreamTarget, Http1ResponseHead responseHead, IReadOnlyList<ProxyHeaderField> responseHeaders, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(upstreamTarget);
        ArgumentNullException.ThrowIfNull(responseHead);
        ArgumentNullException.ThrowIfNull(responseHeaders);
        ArgumentNullException.ThrowIfNull(body);
        var keyCreation = CreateKey(scope, requestHead, upstreamTarget);
        if (keyCreation is not CacheKeyCreation.Created createdKey)
        {
            var rejectionReason = ((CacheKeyCreation.Rejected)keyCreation).Reason;
            if (rejectionReason is not null)
            {
                RecordRejection(rejectionReason);
            }

            return;
        }

        var key = createdKey.Key;
        var responseEligibility = ProxyCacheEligibilityPolicy.EvaluateStoredResponse(scope.Policy, responseHead, body.LongLength);
        if (responseEligibility is not ProxyCacheStorageEligibilityResult.AcceptedResult acceptedStorage)
        {
            RecordRejection(((ProxyCacheStorageEligibilityResult.RejectedResult)responseEligibility).Reason);
            return;
        }

        var ttl = acceptedStorage.Ttl;
        var storedHeaders = SanitizeStoredHeaders(responseHeaders);
        var sizeBytes = CalculateSize(storedHeaders, body);
        if (sizeBytes > scope.Policy.MaxEntryBytes || sizeBytes > scope.Policy.MaxTotalBytes)
        {
            RecordRejection(ProxyCacheEligibilityPolicy.ReasonOversized);
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var contentLength = ProxyResponseContentLengthPolicy.GetContentLength(responseHead, body.LongLength,
            string.Equals(requestHead.Method, "HEAD", StringComparison.OrdinalIgnoreCase));
        var entry = new CacheEntry(key, scope.RouteName, responseHead.StatusCode, responseHead.ReasonPhrase, storedHeaders, body.ToArray(), now, now.Add(ttl), now, sizeBytes, contentLength);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                RemoveEntry(key, existing);
            }

            _entries[key] = entry;
            _approximateBytes += sizeBytes;
            _stores++;
            EvictRouteOverCapacity(scope.RouteName, scope.Policy.MaxTotalBytes);
        }
    }

    public void RecordUncacheable(ProxyCachePolicyFacts policy, string reason)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(reason);
        if (policy.Enabled)
        {
            RecordRejection(reason);
        }
    }

    public ProxyCacheRuntimeStatusSnapshot ReadStatusSnapshot()
    {
        lock (_gate)
        {
            return new ProxyCacheRuntimeStatusSnapshot(_entries.Count, _approximateBytes, _hits, _misses, _stores, _evictions, _storeRejections, _lastClearedAtUtc, _lastClearReason, _rejections.Select(static pair => new ProxyCacheRuntimeRejectionSnapshot(pair.Key, pair.Value)).ToArray(), _entries.Values.Select(static entry => new ProxyCacheRuntimeEntrySnapshot(entry.RouteName, entry.SizeBytes)).ToArray());
        }
    }

    public void Clear(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        lock (_gate)
        {
            _entries.Clear();
            _approximateBytes = 0;
            _lastClearedAtUtc = _timeProvider.GetUtcNow();
            _lastClearReason = reason;
        }
    }

    private void RecordRejection(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        lock (_gate)
        {
            _storeRejections++;
            _rejections.TryGetValue(reason, out var count);
            _rejections[reason] = count + 1;
        }
    }

    private static ProxyHeaderField[] SanitizeStoredHeaders(IReadOnlyList<ProxyHeaderField> headers)
    {
        return headers.Where(static header =>
        {
            ArgumentNullException.ThrowIfNull(header);
            return !Http1ManagedHeaderPolicy.IsManagedStoredResponseHeader(header.Name);
        }).Select(static header => new ProxyHeaderField(header.Name, header.Value)).ToArray();
    }

    private void EvictRouteOverCapacity(string routeName, long maxTotalBytes)
    {
        while (RouteBytes(routeName) > maxTotalBytes)
        {
            var oldest = _entries.Where(entry => string.Equals(entry.Value.RouteName, routeName, StringComparison.OrdinalIgnoreCase)).OrderBy(static entry => entry.Value.LastAccessedAtUtc).FirstOrDefault();
            if (oldest.Value is null)
            {
                return;
            }

            RemoveEntry(oldest.Key, oldest.Value);
            _evictions++;
        }
    }

    private long RouteBytes(string routeName)
    {
        return _entries.Values.Where(entry => string.Equals(entry.RouteName, routeName, StringComparison.OrdinalIgnoreCase)).Sum(static entry => entry.SizeBytes);
    }

    private void RemoveEntry(string key, CacheEntry entry)
    {
        if (_entries.Remove(key))
        {
            _approximateBytes -= entry.SizeBytes;
            if (_approximateBytes < 0)
            {
                _approximateBytes = 0;
            }
        }
    }

    private static long CalculateSize(IReadOnlyList<ProxyHeaderField> headers, byte[] body)
    {
        return body.LongLength + headers.Sum(static header => header.Name.Length + header.Value.Length + 4L);
    }

    private sealed class CacheEntry
    {
        public CacheEntry(string key, string routeName, int statusCode, string reasonPhrase, IReadOnlyList<ProxyHeaderField> headers, byte[] body, DateTimeOffset storedAtUtc, DateTimeOffset expiresAtUtc, DateTimeOffset lastAccessedAtUtc, long sizeBytes, long? contentLength)
        {
            Key = key;
            RouteName = routeName;
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase;
            Headers = headers;
            Body = body;
            StoredAtUtc = storedAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            LastAccessedAtUtc = lastAccessedAtUtc;
            SizeBytes = sizeBytes;
            ContentLength = contentLength;
        }

        public long? ContentLength { get; }
        public string Key { get; }
        public string RouteName { get; }
        public int StatusCode { get; }
        public string ReasonPhrase { get; }
        public IReadOnlyList<ProxyHeaderField> Headers { get; }
        public byte[] Body { get; }
        public DateTimeOffset StoredAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public DateTimeOffset LastAccessedAtUtc { get; private set; }
        public long SizeBytes { get; }

        public void RecordAccess(DateTimeOffset accessedAtUtc)
        {
            LastAccessedAtUtc = accessedAtUtc;
        }

        public CachedProxyResponse ToResponse()
        {
            return new CachedProxyResponse(StatusCode, ReasonPhrase, Headers, Body.ToArray(), StoredAtUtc, ExpiresAtUtc, ContentLength);
        }
    }

    private static CacheKeyCreation CreateKey(ProxyCacheRequestScope scope, Http1RequestHead requestHead, string upstreamTarget)
    {
        var requestEligibility = ProxyCacheEligibilityPolicy.EvaluateRequest(scope.Policy, requestHead);
        if (requestEligibility is ProxyCacheEligibilityResult.RejectedResult rejected)
        {
            return CacheKeyCreation.Reject(rejected.Reason);
        }

        var builder = new StringBuilder();
        AppendPart(builder, scope.RouteName);
        AppendPart(builder, scope.RouteHost);
        AppendPart(builder, requestHead.Method.ToUpperInvariant());
        AppendPart(builder, scope.Scheme);
        AppendPart(builder, requestHead.Host.ToLowerInvariant());
        AppendPart(builder, upstreamTarget);
        foreach (var varyHeader in scope.Policy.VaryByHeaders.OrderBy(static header => header, StringComparer.OrdinalIgnoreCase))
        {
            AppendPart(builder, varyHeader.ToLowerInvariant());
            AppendPart(builder, JoinHeaderValues(requestHead.Headers, varyHeader));
        }

        return CacheKeyCreation.Create(builder.ToString());
    }

    private static string JoinHeaderValues(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        return string.Join("\u001f", headers.Where(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase)).Select(static header => header.Value));
    }

    private static void AppendPart(StringBuilder builder, string value)
    {
        builder.Append(value.Length).Append(':').Append(value).Append('|');
    }

    private abstract record CacheKeyCreation
    {
        private CacheKeyCreation()
        {
        }

        public static Created Create(string key)
        {
            return new Created(key);
        }

        public static Rejected Reject(string reason)
        {
            return new Rejected(reason);
        }

        public sealed record Created : CacheKeyCreation
        {
            public Created(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException("Cache key is required.", nameof(key));
                }

                Key = key;
            }

            public string Key { get; }
        }

        public sealed record Rejected : CacheKeyCreation
        {
            public Rejected(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("Cache key rejection reason is required.", nameof(reason));
                }

                Reason = reason;
            }

            public string Reason { get; }
        }
    }
}
