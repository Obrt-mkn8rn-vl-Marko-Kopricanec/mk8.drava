using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class IpcUploadCostProcessTests(ITestOutputHelper output)
{
    private const int RequestCount = 12;
    private const int Concurrency = 4;
    private const long BodyBytes = 30 * 1024 * 1024;

    [Theory]
    [InlineData("direct")]
    [InlineData("proxy-default")]
    [InlineData("proxy-configured")]
    public async Task KnownLengthConcurrentUploadsRecordExactBytesLatencyAndOwnedProcessMemoryAsync(string profile)
    {
        var originRequests = 0;
        var expectedHash = DevelopmentRepeatedBodyContent.ExpectedHash(BodyBytes);
        var origin = await CreateOriginAsync(expectedHash, () => Interlocked.Increment(ref originRequests)).ConfigureAwait(true);
        await using var originLifetime = origin.ConfigureAwait(true);

        TwoProcessProxy? proxy = null;
        Task? sampling = null;
        using var sampleStop = new CancellationTokenSource();
        var memory = new ConcurrentQueue<MemorySample>();
        var results = new ConcurrentQueue<RequestSample>();
        var duration = Stopwatch.StartNew();
        try
        {
            if (!string.Equals(profile, "direct", StringComparison.Ordinal))
            {
                var settings = string.Equals(profile, "proxy-configured", StringComparison.Ordinal)
                    ? new PrivateHttp2Settings { InitialStreamWindowBytes = 262144, InitialConnectionWindowBytes = 1048576 }
                    : new PrivateHttp2Settings();
                proxy = await TwoProcessProxy.StartAsync(origin.Port, privateHttp2: settings).ConfigureAwait(true);
            }
            using var handler = new SocketsHttpHandler { UseProxy = false, MaxConnectionsPerServer = Concurrency };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new UriBuilder("http", IPAddress.Loopback.ToString(), proxy?.Port ?? origin.Port).Uri,
                Timeout = TimeSpan.FromSeconds(10),
            };
            client.DefaultRequestHeaders.Host = "app.test";
            using var phaseStop = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await SendAsync(client, -1, expectedHash, phaseStop.Token).ConfigureAwait(true);
            if (proxy is not null) sampling = SampleMemoryAsync(proxy, memory, sampleStop.Token);
            duration.Restart();
            await RunRequestsAsync(client, expectedHash, results, phaseStop.Token).ConfigureAwait(true);
            duration.Stop();
            Assert.Equal(RequestCount, results.Count);
            Assert.All(results, static result => Assert.True(result.Succeeded, result.Error));
            Assert.Equal(RequestCount + 1, Volatile.Read(ref originRequests));
        }
        finally
        {
            // Sampling borrows the process fixtures and must settle before either child is retired.
            await sampleStop.CancelAsync().ConfigureAwait(true);
            try { if (sampling is not null) await sampling.ConfigureAwait(true); }
            finally
            {
                WriteEvidence(output, profile, duration.Elapsed.TotalMilliseconds, results, memory, Volatile.Read(ref originRequests));
                if (proxy is not null) await proxy.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    private static Task<DevelopmentHttpUpstream> CreateOriginAsync(string expectedHash, Action observeRequest) =>
        DevelopmentHttpUpstream.StartAsync(async context =>
        {
            Assert.Equal("PUT", context.Request.Method, StringComparer.Ordinal);
            Assert.Equal("app.test", context.Request.Host.Value, StringComparer.Ordinal);
            Assert.Equal(BodyBytes, context.Request.ContentLength);
            var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()
                ?? throw new InvalidOperationException("Origin request-body admission feature is absent.");
            bodyLimit.MaxRequestBodySize = BodyBytes;
            observeRequest();
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
            Assert.Equal(expectedHash, Convert.ToHexString(hash.GetHashAndReset()), StringComparer.Ordinal);
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync(expectedHash, context.RequestAborted).ConfigureAwait(false);
        });

    private static Task RunRequestsAsync(HttpClient client, string expectedHash, ConcurrentQueue<RequestSample> results, CancellationToken cancellationToken)
    {
        var runners = Enumerable.Range(0, Concurrency).Select(worker => RunWorkerAsync(worker, client, expectedHash, results, cancellationToken)).ToArray();
        return Task.WhenAll(runners);
    }

    private static async Task RunWorkerAsync(int worker, HttpClient client, string expectedHash, ConcurrentQueue<RequestSample> results, CancellationToken cancellationToken)
    {
        for (var index = worker; index < RequestCount; index += Concurrency)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var sent = await SendAsync(client, index, expectedHash, cancellationToken).ConfigureAwait(false);
                results.Enqueue(new RequestSample(Index: index, Succeeded: true, SerializedBytes: sent, ElapsedMilliseconds: watch.Elapsed.TotalMilliseconds, Error: null));
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or Xunit.Sdk.XunitException)
            {
                results.Enqueue(new RequestSample(Index: index, Succeeded: false, SerializedBytes: null, ElapsedMilliseconds: watch.Elapsed.TotalMilliseconds, Error: exception.GetType().Name));
            }
        }
    }

    private static async Task<long> SendAsync(HttpClient client, int index, string expectedHash, CancellationToken cancellationToken)
    {
        using var body = new DevelopmentRepeatedBodyContent(BodyBytes);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/ipc-cost/" + index.ToString(CultureInfo.InvariantCulture), UriKind.Relative))
        {
            Content = body, Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedHash, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal);
        Assert.Equal(BodyBytes, body.SerializedBytes);
        return body.SerializedBytes;
    }

    private static async Task SampleMemoryAsync(TwoProcessProxy proxy, ConcurrentQueue<MemorySample> samples, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = proxy.ReadMemory();
                samples.Enqueue(new MemorySample(Stopwatch.GetTimestamp(), value.Application, value.Gateway));
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static void WriteEvidence(ITestOutputHelper output, string profile, double elapsedMilliseconds,
        ConcurrentQueue<RequestSample> samples, ConcurrentQueue<MemorySample> memory, int originRequests)
    {
        var ordered = samples.Where(static value => value.Succeeded).Select(static value => value.ElapsedMilliseconds).Order().ToArray();
        double? Percentile(double fraction) => ordered.Length == 0 ? null : ordered[(int)Math.Ceiling(ordered.Length * fraction) - 1];
        var evidence = new
        {
            SchemaVersion = 1, Profile = profile, ExpectedRequests = RequestCount, Concurrency, BodyBytes,
            RequestDeadlineSeconds = 10, PhaseGuardSeconds = 120, WarmupRequests = 1, OriginRequests = originRequests,
            CompletedRequests = samples.Count, SuccessfulRequests = ordered.Length, FailedRequests = samples.Count - ordered.Length,
            ElapsedMilliseconds = elapsedMilliseconds,
            SuccessfulBytesPerSecond = elapsedMilliseconds > 0 ? ordered.Length * BodyBytes * 1000 / elapsedMilliseconds : (double?)null,
            P50Milliseconds = Percentile(.5), P95Milliseconds = Percentile(.95), P99Milliseconds = Percentile(.99),
            Samples = samples.OrderBy(static value => value.Index).ToArray(), MemorySamples = memory.ToArray(), MemorySamplingMilliseconds = 25,
            Qualification = "Shared development host; repeating32KiB pattern, plain HTTP1, one origin; sampled RSS/private bytes, no continuous peak/upper-bound, production SLA, arbitrary payload, pure-IPC isolation or historical-failure cause claim.",
        };
        output.WriteLine("ipc_upload_cost=" + JsonSerializer.Serialize(evidence));
    }

    private sealed record RequestSample(int Index, bool Succeeded, long? SerializedBytes, double ElapsedMilliseconds, string? Error);
    private sealed record MemorySample(long Timestamp, DevelopmentProcessMemory Application, DevelopmentProcessMemory Gateway);
}
