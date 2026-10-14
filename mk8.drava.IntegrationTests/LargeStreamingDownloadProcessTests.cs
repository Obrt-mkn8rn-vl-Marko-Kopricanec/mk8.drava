using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class LargeStreamingDownloadProcessTests(ITestOutputHelper output)
{
    private const long BodyBytes = 2L * 1024 * 1024 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoGiBResponseStreamsToAnIncrementalReaderAsync(bool configuredCreditWindows)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var expectedHash = DevelopmentRepeatedBodyContent.ExpectedHash(BodyBytes);
        var originSent = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originRequests = 0;
        var origin = await CreateOriginAsync(originSent, () => Interlocked.Increment(ref originRequests)).ConfigureAwait(true);
        await using var originLifetime = origin.ConfigureAwait(true);
        var settings = configuredCreditWindows
            ? new PrivateHttp2Settings { InitialStreamWindowBytes = 262144, InitialConnectionWindowBytes = 1048576 }
            : new PrivateHttp2Settings();
        var proxy = await TwoProcessProxy.StartAsync(origin.Port, privateHttp2: settings).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder("http", IPAddress.Loopback.ToString(), proxy.Port, "/large-stream").Uri)
        {
            Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Host = "app.test";
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal(BodyBytes, response.Content.Headers.ContentLength);
        var received = await VerifyBodyAsync(response.Content, expectedHash, deadline.Token).ConfigureAwait(true);
        Assert.Equal(BodyBytes, await originSent.Task.WaitAsync(deadline.Token).ConfigureAwait(true));
        Assert.Equal(1, Volatile.Read(ref originRequests));
        var memory = proxy.ReadMemory();
        output.WriteLine("large_stream_download=" + JsonSerializer.Serialize(new
        {
            BodyBytes, ReceivedBytes = received, ConfiguredCreditWindows = configuredCreditWindows,
            OriginRequests = originRequests, proxy.EvidenceId, memory.Application, memory.Gateway,
            PhaseGuardSeconds = 120, ResponseHeadGuardSeconds = 60,
            Qualification = "One repeating32KiB HTTP1 response with incremental client hashing; raw process counters only. External owned-cgroup evidence is required for an aggregate memory budget claim.",
        }));
    }

    private static Task<DevelopmentHttpUpstream> CreateOriginAsync(TaskCompletionSource<long> originSent, Action observeRequest) =>
        DevelopmentHttpUpstream.StartAsync(async context =>
        {
            observeRequest();
            Assert.Equal("GET", context.Request.Method, StringComparer.Ordinal);
            context.Response.ContentLength = BodyBytes;
            using var body = new DevelopmentRepeatedBodyContent(BodyBytes);
            await body.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            originSent.TrySetResult(body.SerializedBytes);
        });

    private static async Task<long> VerifyBodyAsync(HttpContent content, string expectedHash, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var lifetime = stream.ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[32 * 1024];
        long received = 0;
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            received += count;
            hash.AppendData(buffer.AsSpan(0, count));
        }
        Assert.Equal(BodyBytes, received);
        Assert.Equal(expectedHash, Convert.ToHexString(hash.GetHashAndReset()), StringComparer.Ordinal);
        return received;
    }
}
