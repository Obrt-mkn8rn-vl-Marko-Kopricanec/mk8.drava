using System.Net;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.CompatibilityTests.LegacyApi.Controllers;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Mk8.Drava.CompatibilityTests.LegacyApi.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class CacheTests
{
    public static async Task CachingDisabledByDefaultAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteSite(temp.Path, "default.json", 18080, 15000);
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        AssertEx.False(ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Routes[0].Cache.Enabled);
    }

    public static async Task DisabledCacheKeepsExistingProxyBehaviorAsync()
    {
        var result = await RunTwoRequestProxyScenarioAsync(cacheEnabled: false, responseFactory: _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 7\r\n\r\nproxied", firstRequest: "GET /resource HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", secondRequest: "GET /resource HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", expectedUpstreamRequests: 2).ConfigureAwait(false);
        AssertEx.Equal(2, result.UpstreamRequests.Count);
        AssertEx.True(result.FirstResponse.EndsWith("proxied", StringComparison.Ordinal), result.FirstResponse);
        AssertEx.True(result.SecondResponse.EndsWith("proxied", StringComparison.Ordinal), result.SecondResponse);
    }

    public static void InvalidCachePolicyIsRejected()
    {
        var validation = new ProxyOptionsValidator(new ProxyEndpointAddressPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy()).Validate(null, new ProxyOptions { Routes = [new ProxyRouteOptions { Name = "bad-cache", Host = "*", PathPrefix = "/", Cache = new ProxyCachePolicyOptions { Enabled = true, MaxEntryBytes = -1, Methods = ["POST"], VaryByHeaders = ["bad header"] }, Upstreams = [new UpstreamOptions { Name = "local", Address = "127.0.0.1", Port = 5000 }] }] });
        AssertEx.True(validation.Failed);
        var failures = AssertEx.NotNull(validation.Failures);
        AssertEx.True(failures.Any(static failure => failure.Contains("MaxEntryBytes", StringComparison.Ordinal)));
        AssertEx.True(failures.Any(static failure => failure.Contains("Methods", StringComparison.Ordinal)));
        AssertEx.True(failures.Any(static failure => failure.Contains("VaryByHeaders", StringComparison.Ordinal)));
    }

    public static async Task EnabledGet200ResponseIsStoredAndServedAsync()
    {
        var result = await RunTwoRequestProxyScenarioAsync(cacheEnabled: true, responseFactory: _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 6\r\nCache-Control: max-age=60\r\n\r\ncached", firstRequest: "GET /resource HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", secondRequest: "GET /resource HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", expectedUpstreamRequests: 1).ConfigureAwait(false);
        AssertEx.Equal(1, result.UpstreamRequests.Count);
        AssertEx.True(result.FirstResponse.EndsWith("cached", StringComparison.Ordinal), result.FirstResponse);
        AssertEx.True(result.SecondResponse.EndsWith("cached", StringComparison.Ordinal), result.SecondResponse);
        AssertEx.True(result.SecondResponse.Contains("Age:", StringComparison.OrdinalIgnoreCase), result.SecondResponse);
    }

    public static async Task HeadResponseReturnsHeadersWithoutBodyAsync()
    {
        var result = await RunTwoRequestProxyScenarioAsync(cacheEnabled: true, responseFactory: _ => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\nX-Head: yes\r\n\r\n", firstRequest: "HEAD /head HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", secondRequest: "HEAD /head HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", expectedUpstreamRequests: 1).ConfigureAwait(false);
        AssertEx.Equal(1, result.UpstreamRequests.Count);
        AssertEx.True(result.SecondResponse.Contains("X-Head: yes", StringComparison.OrdinalIgnoreCase), result.SecondResponse);
        AssertEx.True(result.SecondResponse.EndsWith("\r\n\r\n", StringComparison.Ordinal), result.SecondResponse);
    }

    public static void QueryStringIsPartOfCacheKey()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy());
        var listener = Listener();
        var response = Response("200 OK", [new ProxyHeaderField("Content-Type", "text/plain")]);
        cache.Store(Scope(route, listener), Request("GET", "/item?id=1", "cache.test"), "/item?id=1", response, response.Headers, Encoding.ASCII.GetBytes("one"));
        AssertCacheMiss(cache, route, listener, Request("GET", "/item?id=2", "cache.test"), "/item?id=2");
        var cached = AssertCacheHit(cache, route, listener, Request("GET", "/item?id=1", "cache.test"), "/item?id=1");
        AssertEx.Equal("one", Encoding.ASCII.GetString(cached.Body));
    }

    public static void RewriteTargetIsPartOfCacheKey()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy());
        var listener = Listener();
        var request = Request("GET", "/public/item?id=1", "cache.test");
        var response = Response("200 OK", []);
        cache.Store(Scope(route, listener), request, "/internal/item?id=1", response, response.Headers, Encoding.ASCII.GetBytes("internal"));
        AssertCacheMiss(cache, route, listener, request, "/public/item?id=1");
        var cached = AssertCacheHit(cache, route, listener, request, "/internal/item?id=1");
        AssertEx.Equal("internal", Encoding.ASCII.GetString(cached.Body));
    }

    public static void HostAndVaryHeadersAffectCacheKey()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy(varyByHeaders: ["X-Tenant"]));
        var listener = Listener();
        var response = Response("200 OK", []);
        cache.Store(Scope(route, listener), Request("GET", "/resource", "a.test", [new ProxyHeaderField("X-Tenant", "one")]), "/resource", response, response.Headers, Encoding.ASCII.GetBytes("tenant-one"));
        AssertCacheMiss(cache, route, listener, Request("GET", "/resource", "b.test", [new ProxyHeaderField("X-Tenant", "one")]), "/resource");
        AssertCacheMiss(cache, route, listener, Request("GET", "/resource", "a.test", [new ProxyHeaderField("X-Tenant", "two")]), "/resource");
        var cached = AssertCacheHit(cache, route, listener, Request("GET", "/resource", "a.test", [new ProxyHeaderField("x-tenant", "one")]), "/resource");
        AssertEx.Equal("tenant-one", Encoding.ASCII.GetString(cached.Body));
    }

    public static void AuthorizationRequestIsNotCachedByDefault()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy());
        var listener = Listener();
        var request = Request("GET", "/private", "cache.test", [new ProxyHeaderField("Authorization", "Bearer secret")]);
        var response = Response("200 OK", []);
        cache.Store(Scope(route, listener), request, "/private", response, response.Headers, Encoding.ASCII.GetBytes("private"));
        AssertCacheMiss(cache, route, listener, request, "/private");
        AssertEx.True(CacheStatus(cache, null).StoreRejectionCount > 0);
    }

    public static void CookieRequestIsNotCachedByDefault()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy());
        var listener = Listener();
        var request = Request("GET", "/private", "cache.test", [new ProxyHeaderField("Cookie", "sid=secret")]);
        var response = Response("200 OK", []);
        cache.Store(Scope(route, listener), request, "/private", response, response.Headers, Encoding.ASCII.GetBytes("private"));
        AssertCacheMiss(cache, route, listener, request, "/private");
        var snapshot = CacheStatus(cache, null);
        AssertEx.True(snapshot.Rejections.Any(static rejection => string.Equals(rejection.Reason, "cookie", StringComparison.Ordinal) && rejection.Count == 1));
    }

    public static void CacheEligibilityRejectsCookieBeforeBuffering()
    {
        var route = Route(CachePolicy());
        var request = Request("GET", "/private", "cache.test", [new ProxyHeaderField("Cookie", "sid=secret")]);
        var response = Response("200 OK", []);
        var result = ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(CacheFacts(route.Cache), request, response);
        AssertRejectedEligibility(result, ProxyCacheEligibilityPolicy.ReasonCookie);
    }

    public static void CacheEligibilityResultNamesAcceptedAndRejectedOutcomes()
    {
        var accepted = ProxyCacheEligibilityResult.Accepted();
        var rejected = ProxyCacheEligibilityResult.Rejected(ProxyCacheEligibilityPolicy.ReasonCookie);
        var storageAccepted = ProxyCacheStorageEligibilityResult.Accepted(TimeSpan.FromSeconds(30));
        var storageRejected = ProxyCacheStorageEligibilityResult.Rejected(ProxyCacheEligibilityPolicy.ReasonTtl);
        AssertEx.True(accepted is ProxyCacheEligibilityResult.AcceptedResult);
        AssertRejectedEligibility(rejected, ProxyCacheEligibilityPolicy.ReasonCookie);
        if (storageAccepted is not ProxyCacheStorageEligibilityResult.AcceptedResult acceptedStorage)
        {
            throw new InvalidOperationException("Expected accepted cache storage eligibility.");
        }

        AssertEx.Equal(TimeSpan.FromSeconds(30), acceptedStorage.Ttl);
        if (storageRejected is not ProxyCacheStorageEligibilityResult.RejectedResult rejectedStorage)
        {
            throw new InvalidOperationException("Expected rejected cache storage eligibility.");
        }

        AssertEx.Equal(ProxyCacheEligibilityPolicy.ReasonTtl, rejectedStorage.Reason);
    }

    public static void SetCookieResponseIsNotCachedByDefault()
    {
        AssertRejectedResponse([new ProxyHeaderField("Set-Cookie", "id=1")]);
    }

    public static void NoStoreResponseIsNotCached()
    {
        AssertRejectedResponse([new ProxyHeaderField("Cache-Control", "no-store")]);
    }

    public static void CacheEligibilityRejectsNoStoreBeforeBuffering()
    {
        var route = Route(CachePolicy());
        var request = Request("GET", "/no-store", "cache.test");
        var response = Response("200 OK", [new ProxyHeaderField("Cache-Control", "no-store")]);
        var result = ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(CacheFacts(route.Cache), request, response);
        AssertRejectedEligibility(result, ProxyCacheEligibilityPolicy.ReasonCacheControlNoStore);
    }

    public static void NoCacheResponseIsNotCached()
    {
        AssertRejectedResponse([new ProxyHeaderField("Cache-Control", "no-cache")]);
    }

    public static void MustRevalidateResponseIsNotCached()
    {
        AssertRejectedResponse([new ProxyHeaderField("Cache-Control", "must-revalidate")]);
    }

    public static void PrivateResponseIsNotCachedByDefault()
    {
        AssertRejectedResponse([new ProxyHeaderField("Cache-Control", "private")]);
    }

    public static void MaxAgeControlsTtlAndExpiredEntryIsNotServed()
    {
        var clock = new ManualTimeProvider();
        var cache = new ResponseCacheStore(clock);
        var route = Route(CachePolicy(defaultTtlSeconds: 120));
        var listener = Listener();
        var response = Response("200 OK", [new ProxyHeaderField("Cache-Control", "max-age=1")]);
        var request = Request("GET", "/ttl", "cache.test");
        cache.Store(Scope(route, listener), request, "/ttl", response, response.Headers, Encoding.ASCII.GetBytes("fresh"));
        AssertCacheHit(cache, route, listener, request, "/ttl");
        clock.Advance(TimeSpan.FromSeconds(2));
        AssertCacheMiss(cache, route, listener, request, "/ttl");
    }

    public static void CacheAgeUsesElapsedWholeSecondsAndClampsFutureStoredTime()
    {
        var storedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1);
        AssertEx.Equal(3L, ProxyCacheAgePolicy.CalculateAgeSeconds(storedAtUtc, storedAtUtc.AddSeconds(3.9)));
        AssertEx.Equal(0L, ProxyCacheAgePolicy.CalculateAgeSeconds(storedAtUtc, storedAtUtc.AddSeconds(-1)));
    }

    public static void CachedResponseHeaderPolicyBuildsFramedHeaders()
    {
        var storedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var response = new CachedProxyResponse(200, "OK", [new ProxyHeaderField("Content-Type", "text/plain"), new ProxyHeaderField("Connection", "close"), new ProxyHeaderField("Keep-Alive", "timeout=5"), new ProxyHeaderField("X-Origin", "stored")], Encoding.ASCII.GetBytes("cached-body"), storedAtUtc, storedAtUtc.AddMinutes(1));
        var headers = ProxyCachedResponseHeaderPolicy.BuildFramedResponseHeaders(response, "req-123", storedAtUtc.AddSeconds(3.9));
        AssertEx.True(headers.Any(static header => string.Equals(header.Name, "Content-Type", StringComparison.Ordinal) && string.Equals(header.Value, "text/plain", StringComparison.Ordinal)));
        AssertEx.True(headers.Any(static header => string.Equals(header.Name, "X-Origin", StringComparison.Ordinal) && string.Equals(header.Value, "stored", StringComparison.Ordinal)));
        AssertEx.False(headers.Any(static header => string.Equals(header.Name, "Connection", StringComparison.OrdinalIgnoreCase)));
        AssertEx.False(headers.Any(static header => string.Equals(header.Name, "Keep-Alive", StringComparison.OrdinalIgnoreCase)));
        AssertEx.Equal("3", headers.Single(static header => string.Equals(header.Name, "age", StringComparison.Ordinal)).Value);
        AssertEx.Equal("req-123", headers.Single(static header => string.Equals(header.Name, "x-request-id", StringComparison.Ordinal)).Value);
        AssertEx.Equal("11", headers.Single(static header => string.Equals(header.Name, "content-length", StringComparison.Ordinal)).Value);
    }

    public static void CacheResponseAndStatusCopyInputCollections()
    {
        var storedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var varyByHeaders = new List<string>
        {
            "X-Tenant"
        };
        var cacheableStatusCodes = new List<int>
        {
            200
        };
        var methods = new List<string>
        {
            "GET"
        };
        var policy = new ProxyCachePolicyFacts(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: varyByHeaders, CacheableStatusCodes: cacheableStatusCodes, Methods: methods);
        var headers = new List<ProxyHeaderField>
        {
            new("Content-Type", "text/plain")
        };
        var body = Encoding.ASCII.GetBytes("cached-body");
        var runtimeRejections = new List<ProxyCacheRuntimeRejectionSnapshot>
        {
            new("authorization", 1)
        };
        var runtimeEntries = new List<ProxyCacheRuntimeEntrySnapshot>
        {
            new("api", 11)
        };
        var rejections = new List<ProxyCacheRejectionStatus>
        {
            ProxyCacheRejectionStatus.FromSources("authorization", 1)
        };
        var routes = new List<ProxyCacheRouteStatus>
        {
            ProxyCacheRouteStatus.FromSources("api", enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, currentEntryCount: 1, currentBytes: 11)
        };
        var runtime = new ProxyCacheRuntimeStatusSnapshot(EntryCount: 1, ApproximateBytes: 11, HitCount: 2, MissCount: 3, StoreCount: 4, EvictionCount: 5, StoreRejectionCount: 6, LastClearedAtUtc: null, LastClearReason: null, Rejections: runtimeRejections, Entries: runtimeEntries);
        var response = new CachedProxyResponse(200, "OK", headers, body, storedAtUtc, storedAtUtc.AddMinutes(1));
        var status = ProxyCacheStatus.FromSources(runtime.EntryCount, runtime.ApproximateBytes, runtime.HitCount, runtime.MissCount, runtime.StoreCount, runtime.EvictionCount, runtime.StoreRejectionCount, runtime.LastClearedAtUtc, runtime.LastClearReason, rejections, routes);
        varyByHeaders[0] = "X-Replacement";
        cacheableStatusCodes[0] = 500;
        methods[0] = "POST";
        headers.Clear();
        body[0] = (byte)'X';
        var returnedBody = response.Body;
        returnedBody[0] = (byte)'Y';
        runtimeRejections[0] = new ProxyCacheRuntimeRejectionSnapshot("replacement", 9);
        runtimeEntries[0] = new ProxyCacheRuntimeEntrySnapshot("replacement", 99);
        rejections.Clear();
        routes.Clear();
        varyByHeaders.Clear();
        cacheableStatusCodes.Clear();
        methods.Clear();
        runtimeRejections.Clear();
        runtimeEntries.Clear();
        AssertEx.Equal("X-Tenant", policy.VaryByHeaders[0]);
        AssertEx.Equal(200, policy.CacheableStatusCodes[0]);
        AssertEx.Equal("GET", policy.Methods[0]);
        AssertEx.Equal("Content-Type", response.Headers[0].Name);
        AssertEx.Equal((byte)'c', response.Body[0]);
        AssertEx.Equal("authorization", runtime.Rejections[0].Reason);
        AssertEx.Equal("api", runtime.Entries[0].RouteName);
        AssertEx.Equal("authorization", status.Rejections[0].Reason);
        AssertEx.Equal("api", status.Routes[0].RouteName);
        AssertEx.False(policy.VaryByHeaders is string[], "Cache policy vary headers should not expose a mutable array.");
        AssertEx.False(response.Headers is ProxyHeaderField[], "Cached response headers should not expose a mutable array.");
        AssertEx.False(runtime.Rejections is ProxyCacheRuntimeRejectionSnapshot[], "Cache runtime rejections should not expose a mutable array.");
        AssertEx.False(status.Rejections is ProxyCacheRejectionStatus[], "Cache status rejections should not expose a mutable array.");
        AssertEx.False(status.Routes is ProxyCacheRouteStatus[], "Cache status routes should not expose a mutable array.");
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCachePolicyFacts(Enabled: true, MaxEntryBytes: -1, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCachePolicyFacts(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: -1, DefaultTtl: TimeSpan.FromSeconds(60), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCachePolicyFacts(Enabled: true, MaxEntryBytes: 1024, MaxTotalBytes: 4096, DefaultTtl: TimeSpan.FromTicks(-1), RespectOriginCacheControl: true, VaryByHeaders: [], CacheableStatusCodes: [], Methods: []));
        AssertEx.Throws<ArgumentNullException>(() => new CachedProxyResponse(200, "OK", [null!], body, storedAtUtc, storedAtUtc.AddMinutes(1)));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheRuntimeStatusSnapshot(EntryCount: 0, ApproximateBytes: 0, HitCount: 0, MissCount: 0, StoreCount: 0, EvictionCount: 0, StoreRejectionCount: 0, LastClearedAtUtc: null, LastClearReason: null, Rejections: [null!], Entries: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheRuntimeStatusSnapshot(EntryCount: 0, ApproximateBytes: 0, HitCount: 0, MissCount: 0, StoreCount: 0, EvictionCount: 0, StoreRejectionCount: 0, LastClearedAtUtc: null, LastClearReason: null, Rejections: [], Entries: [null!]));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCacheStatus.FromSources(entryCount: 0, approximateBytes: 0, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [null!], routes: []));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCacheStatus.FromSources(entryCount: 0, approximateBytes: 0, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: [null!]));
        AssertEx.Throws<ArgumentException>(() => new ProxyCacheStatusRouteSource(null!, true, 1024, 4096));
        AssertEx.Throws<ArgumentException>(() => new ProxyCacheStatusRouteSource(" ", true, 1024, 4096));
        AssertEx.Throws<ArgumentException>(() => new ProxyCacheRuntimeRejectionSnapshot(null!, 1));
        AssertEx.Throws<ArgumentException>(() => new ProxyCacheRuntimeRejectionSnapshot(" ", 1));
        AssertEx.Throws<ArgumentException>(() => new ProxyCacheRuntimeEntrySnapshot(null!, 11));
        AssertEx.Throws<ArgumentException>(() => new ProxyCacheRuntimeEntrySnapshot(" ", 11));
        AssertEx.Throws<ArgumentException>(() => ProxyCacheRejectionStatus.FromSources(null!, 1));
        AssertEx.Throws<ArgumentException>(() => ProxyCacheRejectionStatus.FromSources(" ", 1));
        AssertEx.Throws<ArgumentException>(() => ProxyCacheRouteStatus.FromSources(null!, enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, currentEntryCount: 1, currentBytes: 11));
        AssertEx.Throws<ArgumentException>(() => ProxyCacheRouteStatus.FromSources(" ", enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, currentEntryCount: 1, currentBytes: 11));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheStatusRouteSource("api", true, -1, 4096));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheStatusRouteSource("api", true, 1024, -1));
        AssertRuntimeSnapshotRejects(entryCount: -1);
        AssertRuntimeSnapshotRejects(approximateBytes: -1);
        AssertRuntimeSnapshotRejects(hitCount: -1);
        AssertRuntimeSnapshotRejects(missCount: -1);
        AssertRuntimeSnapshotRejects(storeCount: -1);
        AssertRuntimeSnapshotRejects(evictionCount: -1);
        AssertRuntimeSnapshotRejects(storeRejectionCount: -1);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheRuntimeRejectionSnapshot("authorization", -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheRuntimeEntrySnapshot("api", -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyCacheRejectionStatus.FromSources("authorization", -1));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyCacheRouteStatus.FromSources("api", enabled: true, maxEntryBytes: -1, maxTotalBytes: 4096, currentEntryCount: 1, currentBytes: 11));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyCacheRouteStatus.FromSources("api", enabled: true, maxEntryBytes: 1024, maxTotalBytes: -1, currentEntryCount: 1, currentBytes: 11));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyCacheRouteStatus.FromSources("api", enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, currentEntryCount: -1, currentBytes: 11));
        AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyCacheRouteStatus.FromSources("api", enabled: true, maxEntryBytes: 1024, maxTotalBytes: 4096, currentEntryCount: 1, currentBytes: -1));
        AssertCacheStatusRejects(entryCount: -1);
        AssertCacheStatusRejects(approximateBytes: -1);
        AssertCacheStatusRejects(hitCount: -1);
        AssertCacheStatusRejects(missCount: -1);
        AssertCacheStatusRejects(storeCount: -1);
        AssertCacheStatusRejects(evictionCount: -1);
        AssertCacheStatusRejects(storeRejectionCount: -1);
        var statusResponse = ProxyCacheStatusResponseMapper.FromStatus(status);
        AssertEx.False(statusResponse.Rejections is ProxyCacheRejectionStatusResponse[], "Cache API rejections should not expose a mutable array.");
        AssertEx.False(statusResponse.Routes is ProxyCacheRouteStatusResponse[], "Cache API routes should not expose a mutable array.");
        var responseRejections = new List<ProxyCacheRejectionStatusResponse>
        {
            statusResponse.Rejections[0]
        };
        var responseRoutes = new List<ProxyCacheRouteStatusResponse>
        {
            statusResponse.Routes[0]
        };
        var directStatusResponse = new ProxyCacheStatusResponse(entryCount: statusResponse.EntryCount, approximateBytes: statusResponse.ApproximateBytes, hitCount: statusResponse.HitCount, missCount: statusResponse.MissCount, storeCount: statusResponse.StoreCount, evictionCount: statusResponse.EvictionCount, storeRejectionCount: statusResponse.StoreRejectionCount, lastClearedAtUtc: statusResponse.LastClearedAtUtc, lastClearReason: statusResponse.LastClearReason, rejections: responseRejections, routes: responseRoutes);
        responseRejections[0] = responseRejections[0] with
        {
            Reason = "replacement"
        };
        responseRoutes[0] = responseRoutes[0] with
        {
            RouteName = "replacement"
        };
        responseRejections.Clear();
        responseRoutes.Clear();
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheStatusResponse(entryCount: 0, approximateBytes: 0, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: null!, routes: []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheStatusResponse(entryCount: 0, approximateBytes: 0, hitCount: 0, missCount: 0, storeCount: 0, evictionCount: 0, storeRejectionCount: 0, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: null!));
        AssertEx.Equal("authorization", directStatusResponse.Rejections[0].Reason);
        AssertEx.Equal("api", directStatusResponse.Routes[0].RouteName);
        AssertEx.False(directStatusResponse.Rejections is ProxyCacheRejectionStatusResponse[], "Direct cache API rejections should not expose a mutable array.");
        AssertEx.False(directStatusResponse.Routes is ProxyCacheRouteStatusResponse[], "Direct cache API routes should not expose a mutable array.");
        static void AssertRuntimeSnapshotRejects(int entryCount = 0, long approximateBytes = 0, long hitCount = 0, long missCount = 0, long storeCount = 0, long evictionCount = 0, long storeRejectionCount = 0)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => new ProxyCacheRuntimeStatusSnapshot(entryCount, approximateBytes, hitCount, missCount, storeCount, evictionCount, storeRejectionCount, LastClearedAtUtc: null, LastClearReason: null, Rejections: [], Entries: []));
        }

        static void AssertCacheStatusRejects(int entryCount = 0, long approximateBytes = 0, long hitCount = 0, long missCount = 0, long storeCount = 0, long evictionCount = 0, long storeRejectionCount = 0)
        {
            AssertEx.Throws<ArgumentOutOfRangeException>(() => ProxyCacheStatus.FromSources(entryCount, approximateBytes, hitCount, missCount, storeCount, evictionCount, storeRejectionCount, lastClearedAtUtc: null, lastClearReason: null, rejections: [], routes: []));
        }
    }

    public static async Task OversizedResponseIsStreamedButNotCachedAsync()
    {
        var body = "0123456789";
        var result = await RunTwoRequestProxyScenarioAsync(cacheEnabled: true, maxEntryBytes: 4, responseFactory: _ => $"HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n{body}", firstRequest: "GET /big HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", secondRequest: "GET /big HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", expectedUpstreamRequests: 2).ConfigureAwait(false);
        AssertEx.Equal(2, result.UpstreamRequests.Count);
        AssertEx.True(result.FirstResponse.EndsWith(body, StringComparison.Ordinal), result.FirstResponse);
        AssertEx.True(result.SecondResponse.EndsWith(body, StringComparison.Ordinal), result.SecondResponse);
    }

    public static void CacheEligibilityClassifiesUnbufferableFraming()
    {
        var route = Route(CachePolicy());
        var request = Request("GET", "/chunked", "cache.test");
        var response = new Http1ResponseHead("HTTP/1.1", 200, "OK", Http1ResponseFraming.Chunked, [new ProxyHeaderField("Transfer-Encoding", "chunked")]);
        var result = ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(CacheFacts(route.Cache), request, response);
        AssertRejectedEligibility(result, ProxyCacheEligibilityPolicy.ReasonFraming);
    }

    public static void CacheEligibilityClassifiesOversizedContentLength()
    {
        var route = Route(CachePolicy(maxEntryBytes: 4));
        var request = Request("GET", "/big", "cache.test");
        var response = new Http1ResponseHead("HTTP/1.1", 200, "OK", Http1ResponseFraming.FromContentLength(10), [new ProxyHeaderField("Content-Length", "10")]);
        var result = ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(CacheFacts(route.Cache), request, response);
        AssertRejectedEligibility(result, ProxyCacheEligibilityPolicy.ReasonOversized);
    }

    public static void HopByHopHeadersAndTransferEncodingAreNotStored()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy());
        var listener = Listener();
        var request = Request("GET", "/headers", "cache.test");
        var response = Response("200 OK", [new ProxyHeaderField("Connection", "close"), new ProxyHeaderField("Transfer-Encoding", "chunked"), new ProxyHeaderField("X-Stored", "yes")]);
        cache.Store(Scope(route, listener), request, "/headers", response, response.Headers, Encoding.ASCII.GetBytes("headers"));
        var cached = AssertCacheHit(cache, route, listener, request, "/headers");
        var headers = cached.Headers;
        AssertEx.False(headers.Any(static header => string.Equals(header.Name, "Connection", StringComparison.OrdinalIgnoreCase)));
        AssertEx.False(headers.Any(static header => string.Equals(header.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)));
        AssertEx.True(headers.Any(static header => string.Equals(header.Name, "X-Stored", StringComparison.OrdinalIgnoreCase)));
    }

    public static void VaryHeaderCaseAndDuplicateValuesAffectCacheKeyDeterministically()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy(varyByHeaders: ["X-Tenant", "x-tenant"]));
        var listener = Listener();
        var response = Response("200 OK", []);
        cache.Store(Scope(route, listener), Request("GET", "/resource", "cache.test", [new ProxyHeaderField("X-Tenant", "one"), new ProxyHeaderField("x-tenant", "two")]), "/resource", response, response.Headers, Encoding.ASCII.GetBytes("tenant"));
        var cached = AssertCacheHit(cache, route, listener, Request("GET", "/resource", "cache.test", [new ProxyHeaderField("x-tenant", "one"), new ProxyHeaderField("X-Tenant", "two")]), "/resource");
        AssertEx.Equal("tenant", Encoding.ASCII.GetString(cached.Body));
        AssertCacheMiss(cache, route, listener, Request("GET", "/resource", "cache.test", [new ProxyHeaderField("X-Tenant", "one")]), "/resource");
    }

    public static void CacheEvictsOldestEntriesAtMaxTotalBytes()
    {
        var clock = new ManualTimeProvider();
        var cache = new ResponseCacheStore(clock);
        var route = Route(CachePolicy(maxEntryBytes: 128, maxTotalBytes: 15));
        var listener = Listener();
        var response = Response("200 OK", []);
        cache.Store(Scope(route, listener), Request("GET", "/one", "cache.test"), "/one", response, response.Headers, Encoding.ASCII.GetBytes("1111111111"));
        clock.Advance(TimeSpan.FromSeconds(1));
        cache.Store(Scope(route, listener), Request("GET", "/two", "cache.test"), "/two", response, response.Headers, Encoding.ASCII.GetBytes("2222222222"));
        var snapshot = CacheStatus(cache, CreateStoreWithRoute(route).Snapshot);
        AssertEx.Equal(1L, snapshot.EvictionCount);
        AssertCacheMiss(cache, route, listener, Request("GET", "/one", "cache.test"), "/one");
        AssertCacheHit(cache, route, listener, Request("GET", "/two", "cache.test"), "/two");
    }

    public static async Task PartialUpstreamResponseIsNotCachedAsync()
    {
        var result = await RunTwoRequestProxyScenarioAsync(cacheEnabled: true, responseFactory: count => count == 1 ? "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 10\r\nCache-Control: max-age=60\r\n\r\npart" : "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 4\r\nCache-Control: max-age=60\r\n\r\nfull", firstRequest: "GET /partial HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", secondRequest: "GET /partial HTTP/1.1\r\nHost: cache.test\r\nConnection: close\r\n\r\n", expectedUpstreamRequests: 2).ConfigureAwait(false);
        AssertEx.Equal(2, result.UpstreamRequests.Count);
        AssertEx.True(result.FirstResponse.Contains("502 Bad Gateway", StringComparison.Ordinal), result.FirstResponse);
        AssertEx.True(result.SecondResponse.EndsWith("full", StringComparison.Ordinal), result.SecondResponse);
    }

    public static async Task CacheClearEndpointClearsEntriesAsync()
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var store = CreateStoreWithRoute(Route(CachePolicy()));
        var route = store.Snapshot.Routes[0];
        var listener = store.Snapshot.Listeners[0];
        var request = Request("GET", "/clear", "cache.test");
        var response = Response("200 OK", []);
        cache.Store(Scope(route, listener), request, "/clear", response, response.Headers, Encoding.ASCII.GetBytes("clear"));
        var controller = new ProxyCacheController(new ProxyCacheAdministrationService(new ProxyCacheStatusReader(new ProxyCacheStatusConfigurationSource(store), new ProxyCacheRuntimeStatusSource(cache)), cache));
        var status = controller.Clear();
        AssertEx.Equal(0, status.EntryCount);
        AssertEx.Equal("manual", status.LastClearReason);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public static async Task CacheClearEndpointIsProtectedAsync()
    {
        var store = CreateStoreWithAdminAuthentication();
        var audit = new AdminAuditStore(SilentLogPersistenceStore.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/admin/proxy/cache/clear";
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var middleware = ProxyAdminAuthenticationTestFactory.CreateMiddleware(_ => Task.CompletedTask, store, audit);
        await middleware.InvokeAsync(context).ConfigureAwait(false);
        AssertEx.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    public static async Task SuccessfulReloadClearsCacheAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteSite(temp.Path, "reload.json", 18080, 15000);
        var store = new ProxyConfigurationStore();
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var service = new ProxyConfigurationReloadService(CreateLoader(temp.Path), store, store, cache, new ProxyMetrics(), ActivatingProxyListenerReloadApplier.Instance, SilentProxyConfigurationReloadEventSink.Instance, TestHttp3PlatformSupport.ProjectionSource);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        SeedCache(cache, store.Snapshot.Routes[0], store.Snapshot.Listeners[0]);
        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(second);
        var cacheStatus = CacheStatus(cache, store.Snapshot);
        AssertEx.Equal(0, cacheStatus.EntryCount);
        AssertEx.Equal("reload", cacheStatus.LastClearReason);
    }

    public static async Task FailedReloadDoesNotClearCacheAsync()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteSite(temp.Path, "reload.json", 18080, 15000);
        var store = new ProxyConfigurationStore();
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var service = new ProxyConfigurationReloadService(CreateLoader(temp.Path), store, store, cache, new ProxyMetrics(), ActivatingProxyListenerReloadApplier.Instance, SilentProxyConfigurationReloadEventSink.Instance, TestHttp3PlatformSupport.ProjectionSource);
        var first = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Reloaded(first);
        SeedCache(cache, store.Snapshot.Routes[0], store.Snapshot.Listeners[0]);
        ConfigurationTests.WriteCustomSite(temp.Path, "broken.json", "{ nope");
        var second = await service.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationReloadResultAssertions.Failed(second);
        AssertEx.Equal(1, CacheStatus(cache, store.Snapshot).EntryCount);
    }

    public static void CacheStatusReaderShapesRoutesAndRejections()
    {
        var reader = new ProxyCacheStatusReader(new FixedCacheStatusConfigurationSource([new ProxyCacheStatusRouteSource("api", true, 1024, 4096), new ProxyCacheStatusRouteSource("static", false, 512, 2048)]), new FixedCacheRuntimeStatusSource(new ProxyCacheRuntimeStatusSnapshot(3, 19, 5, 2, 3, 1, 2, null, null, [new ProxyCacheRuntimeRejectionSnapshot("oversized", 1), new ProxyCacheRuntimeRejectionSnapshot("authorization", 1)], [new ProxyCacheRuntimeEntrySnapshot("api", 8), new ProxyCacheRuntimeEntrySnapshot("API", 4), new ProxyCacheRuntimeEntrySnapshot("static", 7)])));
        var status = reader.GetStatus();
        AssertEx.Equal(3, status.EntryCount);
        AssertEx.Equal(19L, status.ApproximateBytes);
        AssertEx.Equal("authorization", status.Rejections[0].Reason);
        AssertEx.Equal("oversized", status.Rejections[1].Reason);
        AssertEx.Equal("api", status.Routes[0].RouteName);
        AssertEx.Equal(2, status.Routes[0].CurrentEntryCount);
        AssertEx.Equal(12L, status.Routes[0].CurrentBytes);
        AssertEx.Equal("static", status.Routes[1].RouteName);
        AssertEx.Equal(false, status.Routes[1].Enabled);
        AssertEx.Equal(1, status.Routes[1].CurrentEntryCount);
        AssertEx.Equal(7L, status.Routes[1].CurrentBytes);
        AssertEx.False(status.Rejections is ProxyCacheRejectionStatus[], "Cache status reader rejections should not expose a mutable array.");
        AssertEx.False(status.Routes is ProxyCacheRouteStatus[], "Cache status reader routes should not expose a mutable array.");
    }

    public static void CacheStatusReaderReportsCountersWithoutActiveConfig()
    {
        var reader = new ProxyCacheStatusReader(new FixedCacheStatusConfigurationSource([]), new FixedCacheRuntimeStatusSource(new ProxyCacheRuntimeStatusSnapshot(1, 8, 2, 3, 4, 5, 6, null, null, [], [new ProxyCacheRuntimeEntrySnapshot("orphaned", 8)])));
        var status = reader.GetStatus();
        AssertEx.Equal(1, status.EntryCount);
        AssertEx.Equal(8L, status.ApproximateBytes);
        AssertEx.Equal(2L, status.HitCount);
        AssertEx.Equal(3L, status.MissCount);
        AssertEx.Equal(4L, status.StoreCount);
        AssertEx.Equal(5L, status.EvictionCount);
        AssertEx.Equal(6L, status.StoreRejectionCount);
        AssertEx.Equal(0, status.Routes.Count);
    }

    public static void CacheStatusRouteSourceMapperReadsRoutesWithoutConfigurationSnapshot()
    {
        var disabled = Route(RuntimeCachePolicy.Disabled);
        var enabled = Route(CachePolicy(maxEntryBytes: 4096, maxTotalBytes: 8192), name: "enabled-cache");
        RuntimeRoute[] routes = [disabled, enabled];
        var sources = ProxyCacheStatusRouteSourceMapper.ToRouteSources(routes.Select(static route => route));
        AssertEx.Equal(2, sources.Count);
        AssertEx.Equal("cache", sources[0].RouteName);
        AssertEx.False(sources[0].Enabled);
        AssertEx.Equal(RuntimeCachePolicy.Disabled.MaxEntryBytes, sources[0].MaxEntryBytes);
        AssertEx.Equal(RuntimeCachePolicy.Disabled.MaxTotalBytes, sources[0].MaxTotalBytes);
        AssertEx.Equal("enabled-cache", sources[1].RouteName);
        AssertEx.True(sources[1].Enabled);
        AssertEx.Equal(4096L, sources[1].MaxEntryBytes);
        AssertEx.Equal(8192L, sources[1].MaxTotalBytes);
        AssertEx.False(sources is ProxyCacheStatusRouteSource[], "Cache route sources should not expose a mutable array.");
    }

    public static void CacheStatusRouteSourceMapperRejectsNullRoutes()
    {
        AssertEx.Throws<ArgumentNullException>(() => ProxyCacheStatusRouteSourceMapper.ToRouteSources(null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCacheStatusRouteSourceMapper.ToRouteSources([null!]));
    }

    public static void CacheRuntimeMapperRejectsNullInputs()
    {
        var route = Route(CachePolicy());
        var listener = Listener();
        var policy = CacheFacts(route.Cache);
        AssertEx.Throws<ArgumentNullException>(() => ProxyCacheRuntimeMapper.ToRequestScope(null!, listener));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCacheRuntimeMapper.ToRequestScope(route, null!));
        AssertEx.Throws<ArgumentNullException>(() => ProxyCacheRuntimeMapper.ToPolicyFacts(null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheRequestScope(null!, route.Host, "https", policy));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheRequestScope(route.Name, null!, "https", policy));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheRequestScope(route.Name, route.Host, null!, policy));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyCacheRequestScope(route.Name, route.Host, "https", null!));
    }

    public static void CacheStoreRejectsNullInputs()
    {
        var route = Route(CachePolicy());
        var listener = Listener();
        var scope = Scope(route, listener);
        var request = Request("GET", "/resource", "cache.test");
        var response = Response("200 OK", [new ProxyHeaderField("Content-Type", "text/plain")]);
        var headers = response.Headers;
        var body = Encoding.ASCII.GetBytes("cached");
        var policy = CacheFacts(route.Cache);
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        AssertEx.Throws<ArgumentNullException>(() => new ResponseCacheStore(null!));
        AssertEx.Throws<ArgumentNullException>(() => cache.Get(null!, request, "/resource"));
        AssertEx.Throws<ArgumentNullException>(() => cache.Get(scope, null!, "/resource"));
        AssertEx.Throws<ArgumentNullException>(() => cache.Get(scope, request, null!));
        AssertEx.Throws<ArgumentNullException>(() => cache.Store(null!, request, "/resource", response, headers, body));
        AssertEx.Throws<ArgumentNullException>(() => cache.Store(scope, null!, "/resource", response, headers, body));
        AssertEx.Throws<ArgumentNullException>(() => cache.Store(scope, request, null!, response, headers, body));
        AssertEx.Throws<ArgumentNullException>(() => cache.Store(scope, request, "/resource", null!, headers, body));
        AssertEx.Throws<ArgumentNullException>(() => cache.Store(scope, request, "/resource", response, null!, body));
        AssertEx.Throws<ArgumentNullException>(() => cache.Store(scope, request, "/resource", response, [null!], body));
        AssertEx.Throws<ArgumentNullException>(() => cache.Store(scope, request, "/resource", response, headers, null!));
        AssertEx.Throws<ArgumentNullException>(() => cache.RecordUncacheable(null!, "authorization"));
        AssertEx.Throws<ArgumentNullException>(() => cache.RecordUncacheable(policy, null!));
        AssertEx.Throws<ArgumentNullException>(() => cache.Clear(null!));
    }

    public static void CacheAdministrationClearReturnsPostClearStatus()
    {
        var clearedAtUtc = new DateTimeOffset(2026, 6, 7, 10, 0, 0, TimeSpan.Zero);
        var runtimeSource = new MutableCacheRuntimeStatusSource(new ProxyCacheRuntimeStatusSnapshot(1, 10, 0, 0, 1, 0, 0, null, null, [], [new ProxyCacheRuntimeEntrySnapshot("cache", 10)]));
        var cacheControl = new FixedProxyCacheControl(reason => runtimeSource.Replace(new ProxyCacheRuntimeStatusSnapshot(0, 0, 0, 0, 1, 0, 0, clearedAtUtc, reason, [], [])));
        var service = new ProxyCacheAdministrationService(new ProxyCacheStatusReader(new FixedCacheStatusConfigurationSource([new ProxyCacheStatusRouteSource("cache", true, 1024, 4096)]), runtimeSource), cacheControl);
        var status = service.Clear();
        AssertEx.Equal(1, cacheControl.Reasons.Count);
        AssertEx.Equal("manual", cacheControl.Reasons[0]);
        AssertEx.Equal(0, status.EntryCount);
        AssertEx.Equal(0L, status.ApproximateBytes);
        AssertEx.Equal(clearedAtUtc, status.LastClearedAtUtc);
        AssertEx.Equal("manual", status.LastClearReason);
        AssertEx.Equal(0, status.Routes[0].CurrentEntryCount);
        AssertEx.Equal(0L, status.Routes[0].CurrentBytes);
    }

    private static void AssertRejectedResponse(IReadOnlyList<ProxyHeaderField> responseHeaders)
    {
        var cache = new ResponseCacheStore(new ManualTimeProvider());
        var route = Route(CachePolicy());
        var listener = Listener();
        var request = Request("GET", "/reject", "cache.test");
        var response = Response("200 OK", responseHeaders);
        cache.Store(Scope(route, listener), request, "/reject", response, response.Headers, Encoding.ASCII.GetBytes("reject"));
        AssertCacheMiss(cache, route, listener, request, "/reject");
        AssertEx.True(CacheStatus(cache, null).StoreRejectionCount > 0);
    }

    private static void AssertRejectedEligibility(ProxyCacheEligibilityResult result, string reason)
    {
        if (result is not ProxyCacheEligibilityResult.RejectedResult rejected)
        {
            throw new InvalidOperationException("Expected rejected cache eligibility.");
        }

        AssertEx.Equal(reason, rejected.Reason);
    }

    private static void SeedCache(ResponseCacheStore cache, RuntimeRoute route, RuntimeListener listener)
    {
        var request = Request("GET", "/seed", "cache.test");
        var response = Response("200 OK", []);
        cache.Store(Scope(CreateRoute(route, cache: CachePolicy()), listener), request, "/seed", response, response.Headers, Encoding.ASCII.GetBytes("seed"));
    }

    private static ProxyCacheStatus CacheStatus(ResponseCacheStore cache, ProxyConfigurationSnapshot? configuration)
    {
        return ProxyCacheStatusReader.Project(configuration is null ? [] : ProxyCacheStatusRouteSourceMapper.ToRouteSources(configuration.Routes), cache.ReadStatusSnapshot());
    }

    private static async Task<TwoRequestProxyResult> RunTwoRequestProxyScenarioAsync(bool cacheEnabled, Func<int, string> responseFactory, string firstRequest, string secondRequest, int expectedUpstreamRequests, long maxEntryBytes = 1024 * 1024)
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temp = TemporaryDirectory.Create();
        WriteCacheSite(temp.Path, proxyPort, upstreamPort, cacheEnabled, maxEntryBytes);
        var upstreamTask = RunCountingUpstreamAsync(upstreamPort, expectedUpstreamRequests, responseFactory, timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var first = await SendSingleRequestAsync(proxyPort, firstRequest, timeout.Token).ConfigureAwait(false);
            var second = await SendSingleRequestAsync(proxyPort, secondRequest, timeout.Token).ConfigureAwait(false);
            var requests = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            return new TwoRequestProxyResult(first, second, requests);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> RunCountingUpstreamAsync(int upstreamPort, int expectedRequests, Func<int, string> responseFactory, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, upstreamPort);
        listener.Start();
        List<string> requests = [];
        try
        {
            while (requests.Count < expectedRequests)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var stream = client.GetStream();
                await using var streamDisposal = stream.ConfigureAwait(false);
                var request = await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                requests.Add(request);
                var response = Encoding.ASCII.GetBytes(responseFactory(requests.Count));
                await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }

            return requests;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> SendSingleRequestAsync(int proxyPort, string request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        await using var streamDisposal = stream.ConfigureAwait(false);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
        return await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, bytesRead);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task<string> ReadRequestHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        while (total < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(total, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            total += bytesRead;
            if (total >= 4 && buffer[total - 4] == '\r' && buffer[total - 3] == '\n' && buffer[total - 2] == '\r' && buffer[total - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static void WriteCacheSite(string dataDirectory, int proxyPort, int upstreamPort, bool cacheEnabled, long maxEntryBytes)
    {
        ConfigurationTests.WriteCustomSite(dataDirectory, "cache.json", $$"""
            {
              "name": "cache",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}}
                }
              ],
              "host": "*",
              "pathPrefix": "/",
              "cache": {
                "enabled": {{cacheEnabled.ToString().ToLowerInvariant()}},
                "maxEntryBytes": {{maxEntryBytes}},
                "maxTotalBytes": 1048576,
                "defaultTtlSeconds": 60,
                "respectOriginCacheControl": true
              },
              "upstreams": [
                {
                  "name": "local-test",
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}}
                }
              ]
            }
            """);
    }

    private static IHost BuildProxyHost(string dataDirectory)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Mdrava:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) => services.AddProxyDataPlane(context.Configuration)).Build();
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory)
    {
        var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
        return new ProxyConfigurationLoader(provider, new ProxyDataDirectoryBootstrapper(provider), new SiteConfigurationParser(), new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System);
    }

    private static ProxyConfigurationStore CreateStoreWithRoute(RuntimeRoute route)
    {
        var snapshot = new ProxyConfigurationSnapshot(1, DateTimeOffset.UtcNow, "tests", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []), new RuntimeAdminSecurityOptions([], false, false, null, "MDRAVA_ADMIN_TOKEN", "none", 100), new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), Timeouts(), new RuntimeConnectionLimits(100, 16, 1024), new RuntimeObservabilityOptions(true, 100, new RuntimeLogPersistenceOptions(true, true, 1_048_576, 8)), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)), new RuntimeForwardedHeadersOptions(true, []), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), [Listener()], [route]);
        var store = new ProxyConfigurationStore();
        store.Replace(snapshot);
        return store;
    }

    private static ProxyConfigurationStore CreateStoreWithAdminAuthentication()
    {
        var operationalOptions = new ProxyOperationalOptions
        {
            Admin = new ProxyAdminOptions
            {
                RequireAuthentication = true,
                Token = "cache-admin-token"
            }
        };
        var snapshot = ProxyConfigurationRuntimeMapper.ToRuntimeSnapshot(new ProxyOptions(), operationalOptions, ProxyAdminSecurityTokenPolicy.Resolve(operationalOptions.Admin, static _ => null), new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase), 1, DateTimeOffset.UtcNow, "tests", [], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("tests", "tests/config", "tests/config/sites", "tests/logs", "tests/certs", "tests/state", "tests/config/proxy.json"), [], [], []));
        var store = new ProxyConfigurationStore();
        store.Replace(snapshot);
        return store;
    }

    private static RuntimeRoute Route(RuntimeCachePolicy cache, string name = "cache")
    {
        return new RuntimeRoute(name, "*", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(false, "/health", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1, 1), [], new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), cache, new RuntimeRouteResolvedOptions(104857600, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true));
    }

    private static RuntimeRoute CreateRoute(RuntimeRoute source, RuntimeCachePolicy? cache = null)
    {
        return new RuntimeRoute(source.Name, source.Host, source.PathPrefix, source.Action, source.LoadBalancingPolicy, source.HealthCheck, source.Upstreams, source.HttpsRedirect, source.CanonicalHost, source.HeaderPolicy, source.PathRewrite, source.Redirect, source.StaticResponse, source.Maintenance, cache ?? source.Cache, source.ResolvedOptions, source.SiteName, source.Retry);
    }

    private static RuntimeCachePolicy CachePolicy(int defaultTtlSeconds = 60, IReadOnlyList<string>? varyByHeaders = null, long maxEntryBytes = 1024 * 1024, long maxTotalBytes = 16 * 1024 * 1024)
    {
        return new RuntimeCachePolicy(true, maxEntryBytes, maxTotalBytes, TimeSpan.FromSeconds(defaultTtlSeconds), true, varyByHeaders ?? [], [200], ["GET", "HEAD"]);
    }

    private static RuntimeListener Listener()
    {
        return new RuntimeListener("main", "127.0.0.1", 18080, true, RuntimeListenerTransport.Http, null, [], 512, 32768, 32768, 8192, 8192);
    }

    private static ProxyCacheRequestScope Scope(RuntimeRoute route, RuntimeListener listener)
    {
        return new ProxyCacheRequestScope(route.Name, route.Host, listener.Transport == RuntimeListenerTransport.Https ? "https" : "http", CacheFacts(route.Cache));
    }

    private static ProxyCachePolicyFacts CacheFacts(RuntimeCachePolicy policy)
    {
        return new ProxyCachePolicyFacts(policy.Enabled, policy.MaxEntryBytes, policy.MaxTotalBytes, policy.DefaultTtl, policy.RespectOriginCacheControl, policy.VaryByHeaders, policy.CacheableStatusCodes, policy.Methods);
    }

    private static RuntimeTimeouts Timeouts()
    {
        return new RuntimeTimeouts(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private static Http1RequestHead Request(string method, string target, string host, IReadOnlyList<ProxyHeaderField>? headers = null)
    {
        var requestHeaders = new List<ProxyHeaderField>
        {
            new("Host", host)
        };
        if (headers is not null)
        {
            requestHeaders.AddRange(headers);
        }

        var path = target.Split('?', 2)[0];
        return new Http1RequestHead(method, target, path, "HTTP/1.1", host, Http1RequestFraming.None, requestHeaders);
    }

    private static Http1ResponseHead Response(string status, IReadOnlyList<ProxyHeaderField> headers)
    {
        var split = status.Split(' ', 2);
        return new Http1ResponseHead("HTTP/1.1", int.Parse(split[0]), split.Length > 1 ? split[1] : "", Http1ResponseFraming.FromContentLength(0), headers);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record TwoRequestProxyResult(string FirstResponse, string SecondResponse, IReadOnlyList<string> UpstreamRequests);
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan value)
        {
            _utcNow = _utcNow.Add(value);
        }
    }

    private sealed class FixedCacheStatusConfigurationSource : IProxyCacheStatusConfigurationSource
    {
        private readonly IReadOnlyList<ProxyCacheStatusRouteSource> _routes;
        public FixedCacheStatusConfigurationSource(IReadOnlyList<ProxyCacheStatusRouteSource> routes)
        {
            _routes = routes;
        }

        public IReadOnlyList<ProxyCacheStatusRouteSource> ReadRoutes()
        {
            return _routes;
        }
    }

    private sealed class FixedCacheRuntimeStatusSource : IProxyCacheRuntimeStatusSource
    {
        private readonly ProxyCacheRuntimeStatusSnapshot _snapshot;
        public FixedCacheRuntimeStatusSource(ProxyCacheRuntimeStatusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ProxyCacheRuntimeStatusSnapshot ReadSnapshot()
        {
            return _snapshot;
        }
    }

    private sealed class MutableCacheRuntimeStatusSource : IProxyCacheRuntimeStatusSource
    {
        private ProxyCacheRuntimeStatusSnapshot _snapshot;
        public MutableCacheRuntimeStatusSource(ProxyCacheRuntimeStatusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ProxyCacheRuntimeStatusSnapshot ReadSnapshot()
        {
            return _snapshot;
        }

        public void Replace(ProxyCacheRuntimeStatusSnapshot snapshot)
        {
            _snapshot = snapshot;
        }
    }

    private static CachedProxyResponse AssertCacheHit(ResponseCacheStore cache, RuntimeRoute route, RuntimeListener listener, Http1RequestHead request, string upstreamTarget)
    {
        var lookup = cache.Get(Scope(route, listener), request, upstreamTarget);
        if (lookup is ProxyCacheLookupResult.HitResult hit)
        {
            return hit.Response;
        }

        throw new InvalidOperationException("Expected cached response hit.");
    }

    private static void AssertCacheMiss(ResponseCacheStore cache, RuntimeRoute route, RuntimeListener listener, Http1RequestHead request, string upstreamTarget)
    {
        var lookup = cache.Get(Scope(route, listener), request, upstreamTarget);
        AssertEx.True(lookup is ProxyCacheLookupResult.MissResult);
    }

    private sealed class FixedProxyCacheControl : IProxyCacheControl
    {
        private readonly Action<string> _clear;
        private readonly List<string> _reasons = [];
        public FixedProxyCacheControl(Action<string> clear)
        {
            _clear = clear;
        }

        public IReadOnlyList<string> Reasons => _reasons;

        public void Clear(string reason)
        {
            _reasons.Add(reason);
            _clear(reason);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-cache-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
