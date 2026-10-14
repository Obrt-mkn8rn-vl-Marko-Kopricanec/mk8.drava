using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

internal sealed class RegisteredUploadWorkload
{
    private const long BodyBytes = 8 * 1024 * 1024;
    private const int RequestCount = 12;
    private const int Concurrency = 4;
    private readonly string _expectedHash = DevelopmentRepeatedBodyContent.ExpectedHash(BodyBytes);
    private readonly ConcurrentDictionary<int, int> _originCounts = new();
    private readonly ConcurrentQueue<RequestMeasurement> _samples = new();
    private readonly TaskCompletionSource _arrivals = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _duration = new();
    private int _arrived;
    private int _active;
    private int _gatedConcurrency;

    internal async Task HandleAsync(HttpContext context)
    {
        if (context.Request.Path == "/ready")
        {
            await context.Response.WriteAsync("ready", context.RequestAborted).ConfigureAwait(false);
            return;
        }
        Assert.Equal("PUT", context.Request.Method, StringComparer.Ordinal);
        Assert.Equal(BodyBytes, context.Request.ContentLength);
        Assert.True(int.TryParse(context.Request.Query["id"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index));
        Assert.InRange(index, -1, RequestCount - 1);
        Assert.Equal(1, _originCounts.AddOrUpdate(index, 1, static (_, count) => count + 1));
        if (index >= 0)
        {
            Interlocked.Increment(ref _active);
            if (Interlocked.Increment(ref _arrived) == Concurrency) _arrivals.TrySetResult();
        }
        try
        {
            if (index >= 0) await _release.Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[32 * 1024];
            long received = 0;
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false)) != 0)
            {
                received += count;
                hash.AppendData(buffer.AsSpan(0, count));
            }
            Assert.Equal(BodyBytes, received);
            Assert.Equal(_expectedHash, Convert.ToHexString(hash.GetHashAndReset()), StringComparer.Ordinal);
            context.Response.Headers.Append("X-Development-Request", index.ToString(CultureInfo.InvariantCulture));
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync(_expectedHash, context.RequestAborted).ConfigureAwait(false);
        }
        finally { if (index >= 0) Interlocked.Decrement(ref _active); }
    }

    internal async Task RunAsync(HttpClient client, CancellationToken cancellationToken)
    {
        await SendAsync(client, -1, cancellationToken).ConfigureAwait(true);
        _duration.Restart();
        var tasks = Enumerable.Range(0, Concurrency).Select(index => RunWorkerAsync(client, index, cancellationToken)).ToArray();
        var completion = Task.WhenAll(tasks);
        try
        {
            await _arrivals.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            _gatedConcurrency = Volatile.Read(ref _active);
            Assert.Equal(Concurrency, _gatedConcurrency);
            Assert.False(completion.IsCompleted);
        }
        finally
        {
            _release.TrySetResult();
            try { await completion.ConfigureAwait(true); }
            finally { _duration.Stop(); }
        }
        Assert.Equal(RequestCount, _samples.Count);
        Assert.All(_samples, static sample => Assert.True(sample.Succeeded, sample.Error));
        Assert.Equal(RequestCount + 1, _originCounts.Count);
        Assert.All(_originCounts, static entry => Assert.Equal(1, entry.Value));
        Assert.Equal(0, Volatile.Read(ref _active));
    }

    private async Task RunWorkerAsync(HttpClient client, int worker, CancellationToken cancellationToken)
    {
        for (var index = worker; index < RequestCount; index += Concurrency)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var bytes = await SendAsync(client, index, cancellationToken).ConfigureAwait(false);
                _samples.Enqueue(new RequestMeasurement(index, Succeeded: true, bytes, watch.Elapsed.TotalMilliseconds, Error: null));
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or Xunit.Sdk.XunitException)
            {
                _samples.Enqueue(new RequestMeasurement(index, Succeeded: false, SerializedBytes: null, watch.Elapsed.TotalMilliseconds, exception.GetType().Name));
            }
        }
    }

    private async Task<long> SendAsync(HttpClient client, int index, CancellationToken cancellationToken)
    {
        using var body = new DevelopmentRepeatedBodyContent(BodyBytes);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/cost?id=" + index.ToString(CultureInfo.InvariantCulture), UriKind.Relative)) { Content = body, Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(_expectedHash, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
        Assert.Equal([index.ToString(CultureInfo.InvariantCulture)], [.. response.Headers.GetValues("X-Development-Request")]);
        Assert.Equal(BodyBytes, body.SerializedBytes);
        return body.SerializedBytes;
    }

    internal void WriteEvidence(ITestOutputHelper output, bool relay,
        (DevelopmentProcessMemory Application, DevelopmentProcessMemory Gateway) hosts, DevelopmentProcessMemory? agent, int sockets)
    {
        var ordered = _samples.Where(static value => value.Succeeded).Select(static value => value.ElapsedMilliseconds).Order().ToArray();
        double? Percentile(double fraction) => ordered.Length == 0 ? null : ordered[(int)Math.Ceiling(ordered.Length * fraction) - 1];
        output.WriteLine("registered_transport_cost=" + JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Profile = relay ? "registered-relay" : "registered-local", BodyBytes, RequestCount, Concurrency,
            GatedConcurrency = _gatedConcurrency, FrontendProtocol = "HTTP2 over TLS", OriginProtocol = "HTTP1",
            RequestDeadlineSeconds = 10, PhaseGuardSeconds = 120, WarmupRequests = 1,
            CompletedRequests = _samples.Count, SuccessfulRequests = ordered.Length, FailedRequests = _samples.Count - ordered.Length,
            ElapsedMilliseconds = _duration.Elapsed.TotalMilliseconds,
            SuccessfulBytesPerSecond = _duration.Elapsed.TotalMilliseconds > 0 ? ordered.Length * BodyBytes * 1000 / _duration.Elapsed.TotalMilliseconds : (double?)null,
            P50Milliseconds = Percentile(.5), P95Milliseconds = Percentile(.95), P99Milliseconds = Percentile(.99), ConnectedSockets = sockets,
            OperatingSystem = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), AvailableLogicalProcessors = Environment.ProcessorCount,
            GcAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            OriginCounts = _originCounts.OrderBy(static pair => pair.Key).ToArray(), Samples = _samples.OrderBy(static value => value.Index).ToArray(),
            hosts.Application, hosts.Gateway, Agent = agent,
            Qualification = "Synthetic shared-host registered uploads; repeated32KiB pattern. First4requests gated concurrently; nearest-rank latency includes gatewait, excludesstartup/registration/warmup. Relay uses an owned logicalremote node on the same machine. Raw final process counters include startup; no pureIPC/network isolation, lifetimepeak, memoryupperbound, productionSLA, Windows or historicalcauseclaim.",
        }));
    }

    private sealed record RequestMeasurement(int Index, bool Succeeded, long? SerializedBytes, double ElapsedMilliseconds, string? Error);
}
