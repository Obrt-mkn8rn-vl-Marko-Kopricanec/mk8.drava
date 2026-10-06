using System.Net;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class HardeningTests
{
    public static void AdmissionControllerEnforcesClientLimit()
    {
        var metrics = new ProxyMetrics();
        var admission = new ProxyAdmissionController(metrics);
        using var first = AcceptedLease(admission.AcquireClientConnection(1));
        var second = admission.AcquireClientConnection(1);
        AssertRejected(second);
        AssertEx.Equal(1L, metrics.Snapshot().Rejections.ClientConnectionAdmissionRejections);
    }

    public static void AdmissionLeaseDisposalReleasesClientSlot()
    {
        var metrics = new ProxyMetrics();
        var admission = new ProxyAdmissionController(metrics);
        using (var lease = AcceptedLease(admission.AcquireClientConnection(1)))
        {
            AssertEx.Equal(1, admission.ActiveClientConnections);
            AssertRejected(admission.AcquireClientConnection(1));
        }

        using var reacquired = AcceptedLease(admission.AcquireClientConnection(1));
        AssertEx.Equal(1, admission.ActiveClientConnections);
    }

    public static void AdmissionControllerEnforcesTlsHandshakeLimit()
    {
        var metrics = new ProxyMetrics();
        var admission = new ProxyAdmissionController(metrics);
        using var first = AcceptedLease(admission.AcquireTlsHandshake(1));
        var second = admission.AcquireTlsHandshake(1);
        AssertRejected(second);
        AssertEx.Equal(1L, metrics.Snapshot().Tls.HandshakeAdmissionRejections);
        AssertEx.Equal(1L, metrics.Snapshot().Tls.ActiveHandshakes);
    }

    public static void AdmissionLeaseDisposalReleasesTlsHandshakeSlot()
    {
        var metrics = new ProxyMetrics();
        var admission = new ProxyAdmissionController(metrics);
        using (var lease = AcceptedLease(admission.AcquireTlsHandshake(1)))
        {
            AssertEx.Equal(1, admission.ActiveTlsHandshakes);
            AssertEx.Equal(1L, metrics.Snapshot().Tls.ActiveHandshakes);
            AssertRejected(admission.AcquireTlsHandshake(1));
        }

        using var reacquired = AcceptedLease(admission.AcquireTlsHandshake(1));
        AssertEx.Equal(1, admission.ActiveTlsHandshakes);
        AssertEx.Equal(1L, metrics.Snapshot().Tls.ActiveHandshakes);
    }

    public static void RateLimiterEnforcesRequestLimitAndRefills()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero));
        var metrics = new ProxyMetrics();
        var limiter = new ClientRateLimiter(metrics, clock);
        var ip = "127.0.0.1";
        AssertAccepted(limiter.AcquireRequest(ip, 1));
        AssertRejected(limiter.AcquireRequest(ip, 1));
        clock.Advance(TimeSpan.FromSeconds(61));
        AssertAccepted(limiter.AcquireRequest(ip, 1));
        AssertEx.Equal(1L, metrics.Snapshot().Rejections.RateLimitedRequests);
    }

    public static void ConcurrentRateLimiterBoundaryAllowsOnlyConfiguredLimit()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 10, 10, 5, 0, TimeSpan.Zero));
        var metrics = new ProxyMetrics();
        var limiter = new ClientRateLimiter(metrics, clock);
        var ip = "127.0.0.1";
        var allowed = 0;
        Parallel.For(0, 32, _ =>
        {
            if (limiter.AcquireRequest(ip, 5) is ClientRateLimitDecision.AcceptedResult)
            {
                Interlocked.Increment(ref allowed);
            }
        });
        AssertEx.Equal(5, allowed);
        AssertEx.Equal(27L, metrics.Snapshot().Rejections.RateLimitedRequests);
    }

    public static void RateLimiterEnforcesUpgradeLimit()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 10, 10, 10, 0, TimeSpan.Zero));
        var metrics = new ProxyMetrics();
        var limiter = new ClientRateLimiter(metrics, clock);
        var ip = "127.0.0.1";
        AssertAccepted(limiter.AcquireUpgrade(ip, 1));
        AssertRejected(limiter.AcquireUpgrade(ip, 1));
        AssertEx.Equal(1L, metrics.Snapshot().Rejections.RateLimitedUpgrades);
    }

    public static void RateLimiterUsesNormalizedClientAddressKeys()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 10, 10, 15, 0, TimeSpan.Zero));
        var metrics = new ProxyMetrics();
        var limiter = new ClientRateLimiter(metrics, clock);
        var firstKey = ProxyClientAddressPolicy.NormalizeRequiredClientIp(IPAddress.Parse("127.0.0.1"));
        var secondKey = ProxyClientAddressPolicy.NormalizeRequiredClientIp(IPAddress.Parse("::ffff:127.0.0.1"));
        AssertAccepted(limiter.AcquireRequest(firstKey, 1));
        AssertRejected(limiter.AcquireRequest(secondKey, 1));
        AssertEx.Equal(1, limiter.EntryCount);
        AssertEx.Equal(1L, metrics.Snapshot().Rejections.RateLimitedRequests);
    }

    public static void RateLimiterCleansStaleEntries()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 10, 10, 20, 0, TimeSpan.Zero));
        var metrics = new ProxyMetrics();
        var limiter = new ClientRateLimiter(metrics, clock);
        for (var index = 0; index < 300; index++)
        {
            limiter.AcquireRequest($"10.0.0.{index % 250}", 10);
        }

        AssertEx.True(limiter.EntryCount > 0);
        clock.Advance(TimeSpan.FromMinutes(6));
        for (var index = 0; index < 256; index++)
        {
            limiter.AcquireRequest($"10.1.0.{index % 250}", 10);
        }

        AssertEx.True(limiter.EntryCount < 300);
    }

    private static void AssertAccepted(ClientRateLimitDecision decision)
    {
        AssertEx.True(decision is ClientRateLimitDecision.AcceptedResult);
    }

    private static void AssertRejected(ClientRateLimitDecision decision)
    {
        AssertEx.True(decision is ClientRateLimitDecision.RejectedResult);
    }

    private static AdmissionLease AcceptedLease(ProxyAdmissionDecision decision)
    {
        if (decision is ProxyAdmissionDecision.AcceptedResult accepted)
        {
            return accepted.Lease;
        }

        throw new InvalidOperationException("Expected accepted admission decision.");
    }

    private static void AssertRejected(ProxyAdmissionDecision decision)
    {
        AssertEx.True(decision is ProxyAdmissionDecision.RejectedResult);
    }

    public static void ShutdownCoordinatorExposesGraceDeadlineAndCancels()
    {
        var startedAtUtc = new DateTimeOffset(2026, 6, 10, 10, 25, 0, TimeSpan.Zero);
        using var coordinator = new ProxyShutdownCoordinator(new ManualTimeProvider(startedAtUtc));
        var token = coordinator.BeginShutdown(TimeSpan.FromMilliseconds(100));
        AssertEx.True(coordinator.IsShuttingDown);
        AssertEx.Equal(startedAtUtc, coordinator.StartedAtUtc);
        AssertEx.Equal(startedAtUtc.AddMilliseconds(100), coordinator.DeadlineUtc);
        AssertEx.False(token.IsCancellationRequested);
        Thread.Sleep(250);
        AssertEx.True(token.IsCancellationRequested);
    }

    public static void ShutdownCoordinatorBeginShutdownIsIdempotent()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 10, 10, 30, 0, TimeSpan.Zero));
        using var coordinator = new ProxyShutdownCoordinator(clock);
        var firstToken = coordinator.BeginShutdown(TimeSpan.FromSeconds(5));
        var firstStartedAtUtc = coordinator.StartedAtUtc;
        var firstDeadlineUtc = coordinator.DeadlineUtc;
        clock.Advance(TimeSpan.FromMinutes(1));
        var secondToken = coordinator.BeginShutdown(TimeSpan.FromMinutes(1));
        AssertEx.Equal(firstToken, secondToken);
        AssertEx.Equal(firstStartedAtUtc, coordinator.StartedAtUtc);
        AssertEx.Equal(firstDeadlineUtc, coordinator.DeadlineUtc);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly Lock _gate = new();
        private DateTimeOffset _utcNow;
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        public void Advance(TimeSpan interval)
        {
            lock (_gate)
            {
                _utcNow = _utcNow.Add(interval);
            }
        }
    }
}
