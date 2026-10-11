using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class RegisteredReplicaBalancingProcessTests(ITestOutputHelper output)
{
    private const int ConcurrentRequests = 8;
    [Fact]
    public async Task MultiplexedRequestsAvoidAnOccupiedReplicaAndDrainPreservesItsAcceptedStreamAsync()
    {
        var workload = new ReplicaWorkload();
        var first = await DevelopmentHttpUpstream.StartAsync(workload.Handler("first")).ConfigureAwait(true);
        await using var firstLifetime = first.ConfigureAwait(true);
        var second = await DevelopmentHttpUpstream.StartAsync(workload.Handler("second")).ConfigureAwait(true);
        await using var secondLifetime = second.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(first.Port, enrolledSite: true, manualRoute: false, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var registration = new DevelopmentRegistrationClient(proxy);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var firstIdentity = Identity();
        var secondIdentity = Identity();
        await registration.SubmitAsync(Register(firstIdentity, first.Port), deadline.Token).ConfigureAwait(true);
        await registration.SubmitAsync(Register(secondIdentity, second.Port), deadline.Token).ConfigureAwait(true);
        await WaitReadyAsync(registration, firstIdentity, deadline.Token).ConfigureAwait(true);
        await WaitReadyAsync(registration, secondIdentity, deadline.Token).ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        try
        {
            await VerifyHeldSelectionAndDrainAsync(client, registration, firstIdentity, secondIdentity, workload, deadline.Token).ConfigureAwait(true);
        }
        finally
        {
            WriteTimingEvidence(output, workload, client.ConnectedSockets);
        }
    }

    private static async Task VerifyHeldSelectionAndDrainAsync(DevelopmentSiteClient client, DevelopmentRegistrationClient registration,
        RegistrationIdentity first, RegistrationIdentity second, ReplicaWorkload workload, CancellationToken cancellationToken)
    {
        var held = ReadHeldResponseAsync(client.Client, workload, cancellationToken);
        try
        {
            await workload.PublicHead.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            var occupied = await workload.Occupied.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            var available = string.Equals(occupied, "first", StringComparison.Ordinal) ? "second" : "first";
            Assert.False(held.IsCompleted);
            await VerifyFollowingRequestsAsync(client.Client, available, workload, "before-drain", cancellationToken).ConfigureAwait(true);
            await VerifyConcurrentReservationsAsync(client.Client, occupied, workload, cancellationToken).ConfigureAwait(true);
            var identity = string.Equals(occupied, "first", StringComparison.Ordinal) ? first : second;
            var drained = await registration.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Drain }, cancellationToken).ConfigureAwait(true);
            Assert.Equal(RegistrationPhase.Draining, drained.Phase);
            await VerifyFollowingRequestsAsync(client.Client, available, workload, "after-drain", cancellationToken).ConfigureAwait(true);
            Assert.False(held.IsCompleted);
            Assert.Equal(1, client.ConnectedSockets);
        }
        finally
        {
            workload.Release.TrySetResult();
            await held.ConfigureAwait(true);
        }
    }

    private static async Task ReadHeldResponseAsync(HttpClient client, ReplicaWorkload workload, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri("/held", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        workload.PublicHead.TrySetResult();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
    }

    private static async Task VerifyFollowingRequestsAsync(HttpClient client, string expected, ReplicaWorkload workload, string phase, CancellationToken cancellationToken)
    {
        // Each unknown-length response must reach HTTP/2 END_STREAM, after private
        // exchange settlement, before selecting against the still-occupied replica again.
        for (var index = 0; index < 12; index++)
        {
            var started = Stopwatch.GetTimestamp();
            var succeeded = false;
            try
            {
                Assert.Equal(expected, await ReadReplicaAsync(client, cancellationToken).ConfigureAwait(true));
                succeeded = true;
            }
            finally
            {
                workload.Timings.Enqueue(new RequestTiming(phase, Stopwatch.GetElapsedTime(started).TotalMilliseconds, succeeded));
            }
        }
    }

    private static async Task<string> ReadReplicaAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri("/short", UriKind.Relative), cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
    }

    private static async Task VerifyConcurrentReservationsAsync(HttpClient client, string occupied, ReplicaWorkload workload, CancellationToken cancellationToken)
    {
        var requests = new List<Task<string>>();
        for (var index = 0; index < ConcurrentRequests; index++) requests.Add(ReadConcurrentResponseAsync(client, index, workload, cancellationToken));
        var completion = Task.WhenAll(requests);
        string[] responses;
        try
        {
            await workload.ConcurrentArrivals.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            var firstActive = workload.FirstConcurrent + (string.Equals(occupied, "first", StringComparison.Ordinal) ? 1 : 0);
            var secondActive = workload.SecondConcurrent + (string.Equals(occupied, "second", StringComparison.Ordinal) ? 1 : 0);
            Assert.Equal(ConcurrentRequests, workload.FirstConcurrent + workload.SecondConcurrent);
            Assert.InRange(Math.Abs(firstActive - secondActive), 0, 1);
            Assert.False(completion.IsCompleted);
        }
        finally
        {
            workload.ConcurrentRelease.TrySetResult();
            responses = await completion.ConfigureAwait(true);
        }
        Assert.Equal(workload.FirstConcurrent, responses.Count(static value => string.Equals(value, "first", StringComparison.Ordinal)));
        Assert.Equal(workload.SecondConcurrent, responses.Count(static value => string.Equals(value, "second", StringComparison.Ordinal)));
    }

    private static async Task<string> ReadConcurrentResponseAsync(HttpClient client, int index, ReplicaWorkload workload, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var id = index.ToString(CultureInfo.InvariantCulture);
            using var response = await client.GetAsync(new Uri("/concurrent?id=" + id, UriKind.Relative), cancellationToken).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpVersion.Version20, response.Version);
            // xUnit Single verifies header cardinality; First would accept duplicate values.
#pragma warning disable HLQ005
            Assert.Equal(id, Assert.Single(response.Headers.GetValues("X-Development-Request")));
#pragma warning restore HLQ005
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            Assert.True(body is "first" or "second");
            succeeded = true;
            return body;
        }
        finally
        {
            workload.Timings.Enqueue(new RequestTiming("concurrent-gated", Stopwatch.GetElapsedTime(started).TotalMilliseconds, succeeded));
        }
    }

    private static RegistrationIdentity Identity() => new()
    {
        SiteId = "development", NodeId = "local", OwnerId = "development", ServiceId = "svc", ContractId = "v1",
        InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N"),
    };

    private static RegistrationCommand Register(RegistrationIdentity identity, int port) => new()
    {
        Identity = identity, Operation = RegistrationOperation.Register,
        Advertisement = new ServiceAdvertisement { DeploymentId = "development", Address = "127.0.0.1", Port = port, ReadinessPath = "/ready" },
    };

    private static async Task WaitReadyAsync(DevelopmentRegistrationClient client, RegistrationIdentity identity, CancellationToken cancellationToken)
    {
        while (true)
        {
            var status = await client.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Status }, cancellationToken).ConfigureAwait(true);
            if (status.Phase == RegistrationPhase.Ready) return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(true);
        }
    }

    private static void WriteTimingEvidence(ITestOutputHelper output, ReplicaWorkload workload, int connectedSockets)
    {
        ArgumentNullException.ThrowIfNull(output);
        var successful = new List<double>();
        var succeeded = 0;
        foreach (var timing in workload.Timings)
        {
            if (!timing.Succeeded) continue;
            succeeded++;
            if (!string.Equals(timing.Phase, "concurrent-gated", StringComparison.Ordinal)) successful.Add(timing.ElapsedMilliseconds);
        }
        successful.Sort();
        var evidence = new
        {
            SchemaVersion = 1,
            FrontendProtocol = "HTTP/2 over TLS",
            PrivateProtocol = "authenticated gRPC",
            OriginProtocol = "HTTP/1.1",
            ShortRequestConcurrency = 1,
            HeldRequestConcurrency = 1,
            ExpectedTimedRequests = 24 + ConcurrentRequests,
            CompletedTimedRequests = workload.Timings.Count,
            SuccessfulTimedRequests = succeeded,
            FailedTimedRequests = workload.Timings.Count - succeeded,
            OrdinaryLatencySamples = successful.Count,
            GatedRequestConcurrency = ConcurrentRequests,
            FirstGatedRequests = workload.FirstConcurrent,
            SecondGatedRequests = workload.SecondConcurrent,
            ConnectedSockets = connectedSockets,
            PercentileMethod = "nearest rank, successful ordinary client requests through full body and selection assertions; deliberate gate waits excluded",
            P50Milliseconds = Percentile(successful, 50),
            P95Milliseconds = Percentile(successful, 95),
            P99Milliseconds = Percentile(successful, 99),
            OperatingSystem = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            AvailableLogicalProcessors = Environment.ProcessorCount,
            GcAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            Samples = workload.Timings,
        };
        output.WriteLine("replica_timing=" + JsonSerializer.Serialize(evidence));
    }

    private static double? Percentile(List<double> sorted, int percent) => sorted.Count == 0
        ? null : sorted[(int)Math.Ceiling(sorted.Count * percent / 100.0) - 1];

    private sealed record RequestTiming(string Phase, double ElapsedMilliseconds, bool Succeeded);

    private sealed class ReplicaWorkload
    {
        private int _firstConcurrent;
        private int _secondConcurrent;
        private int _concurrentStarted;
        private readonly ConcurrentDictionary<string, byte> _requestIds = new(StringComparer.Ordinal);
        public int FirstConcurrent => Volatile.Read(ref _firstConcurrent);
        public int SecondConcurrent => Volatile.Read(ref _secondConcurrent);
        public ConcurrentQueue<RequestTiming> Timings { get; } = new();
        public TaskCompletionSource ConcurrentArrivals { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConcurrentRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Occupied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PublicHead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RequestDelegate Handler(string replica) => async context =>
        {
            if (context.Request.Path == "/held")
            {
                context.Response.ContentLength = 2;
                await context.Response.WriteAsync("o", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                Occupied.TrySetResult(replica);
                await Release.Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsync("k", context.RequestAborted).ConfigureAwait(false);
            }
            else if (context.Request.Path == "/concurrent")
            {
                var id = context.Request.Query["id"].ToString();
                Assert.True(_requestIds.TryAdd(id, 0));
                context.Response.Headers["X-Development-Request"] = id;
                await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);
                if (string.Equals(replica, "first", StringComparison.Ordinal)) Interlocked.Increment(ref _firstConcurrent);
                else Interlocked.Increment(ref _secondConcurrent);
                if (Interlocked.Increment(ref _concurrentStarted) == ConcurrentRequests) ConcurrentArrivals.TrySetResult();
                await ConcurrentRelease.Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsync(replica, context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await context.Response.StartAsync(context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsync(context.Request.Path == "/ready" ? "ready" : replica, context.RequestAborted).ConfigureAwait(false);
            }
        };
    }
}
