namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public sealed class ClientRateLimiter
{
    private static readonly TimeSpan StaleEntryAge = TimeSpan.FromMinutes(5);
    private readonly IProxyRateLimitMetricsSink _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, BucketSet> _buckets = new(StringComparer.Ordinal);
    private long _operationCount;
    public ClientRateLimiter(IProxyRateLimitMetricsSink metrics, TimeProvider timeProvider)
    {
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    public ClientRateLimitDecision AcquireRequest(string? clientAddressKey, int perMinuteLimit)
    {
        if (string.IsNullOrWhiteSpace(clientAddressKey))
        {
            return ClientRateLimitDecision.Accepted;
        }

        var allowed = TryAcquire(clientAddressKey, perMinuteLimit, static buckets => buckets.Requests);
        if (!allowed)
        {
            _metrics.RequestRateLimited();
            return ClientRateLimitDecision.Rejected;
        }

        return ClientRateLimitDecision.Accepted;
    }

    public ClientRateLimitDecision AcquireUpgrade(string? clientAddressKey, int perMinuteLimit)
    {
        if (string.IsNullOrWhiteSpace(clientAddressKey))
        {
            return ClientRateLimitDecision.Accepted;
        }

        var allowed = TryAcquire(clientAddressKey, perMinuteLimit, static buckets => buckets.Upgrades);
        if (!allowed)
        {
            _metrics.UpgradeRateLimited();
            return ClientRateLimitDecision.Rejected;
        }

        return ClientRateLimitDecision.Accepted;
    }

    public int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _buckets.Count;
            }
        }
    }

    private bool TryAcquire(string clientAddressKey, int perMinuteLimit, Func<BucketSet, TokenBucket> selectBucket)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (!_buckets.TryGetValue(clientAddressKey, out var buckets))
            {
                buckets = new BucketSet(now);
                _buckets.Add(clientAddressKey, buckets);
            }

            buckets.RecordSeen(now);
            var bucket = selectBucket(buckets);
            var allowed = bucket.TryConsume(now, perMinuteLimit);
            if (Interlocked.Increment(ref _operationCount) % 256 == 0)
            {
                Cleanup(now);
            }

            return allowed;
        }
    }

    private void Cleanup(DateTimeOffset now)
    {
        var staleKeys = _buckets.Where(pair => now - pair.Value.LastSeenUtc > StaleEntryAge).Select(static pair => pair.Key).ToArray();
        foreach (var staleKey in staleKeys)
        {
            _buckets.Remove(staleKey);
        }
    }

    private sealed class BucketSet
    {
        public BucketSet(DateTimeOffset now)
        {
            LastSeenUtc = now;
            Requests = new TokenBucket(now);
            Upgrades = new TokenBucket(now);
        }

        public DateTimeOffset LastSeenUtc { get; private set; }
        public TokenBucket Requests { get; }
        public TokenBucket Upgrades { get; }

        public void RecordSeen(DateTimeOffset seenAtUtc)
        {
            LastSeenUtc = seenAtUtc;
        }
    }

    private sealed class TokenBucket
    {
        private double _tokens;
        private DateTimeOffset _lastRefillUtc;
        private bool _initialized;
        public TokenBucket(DateTimeOffset now)
        {
            _lastRefillUtc = now;
        }

        public bool TryConsume(DateTimeOffset now, int perMinuteLimit)
        {
            var capacity = Math.Max(1, perMinuteLimit);
            if (!_initialized)
            {
                _tokens = capacity;
                _initialized = true;
            }

            var elapsedSeconds = Math.Max(0, (now - _lastRefillUtc).TotalSeconds);
            _tokens = Math.Min(capacity, _tokens + elapsedSeconds * capacity / 60d);
            _lastRefillUtc = now;
            if (_tokens < 1d)
            {
                return false;
            }

            _tokens -= 1d;
            return true;
        }
    }
}
