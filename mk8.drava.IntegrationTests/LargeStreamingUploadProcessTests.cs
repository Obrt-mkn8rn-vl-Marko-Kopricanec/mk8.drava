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
public sealed class LargeStreamingUploadProcessTests(ITestOutputHelper output)
{
    private const long BodyBytes = 2L * 1024 * 1024 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoGiBUploadReachesGatedOriginBeforeSerializationCompletesAsync(bool configuredCreditWindows)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectedHash = DevelopmentRepeatedBodyContent.ExpectedHash(BodyBytes);
        var originRequests = 0;
        var origin = await CreateOriginAsync(expectedHash, firstRead, allowRead, () => Interlocked.Increment(ref originRequests)).ConfigureAwait(true);
        await using var originLifetime = origin.ConfigureAwait(true);
        var settings = configuredCreditWindows
            ? new PrivateHttp2Settings { InitialStreamWindowBytes = 262144, InitialConnectionWindowBytes = 1048576 }
            : new PrivateHttp2Settings();
        var proxy = await TwoProcessProxy.StartAsync(origin.Port,
            requestBodyLimits: [new GatewayRequestBodyLimit { Host = "app.test", MaxRequestBodyBytes = BodyBytes }],
            routeBodyLimit: BodyBytes, privateHttp2: settings).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        using var body = new DevelopmentRepeatedBodyContent(BodyBytes);
        using var request = new HttpRequestMessage(HttpMethod.Put, new UriBuilder("http", IPAddress.Loopback.ToString(), proxy.Port, "/large-stream").Uri)
        {
            Content = body, Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Host = "app.test";
        // The finally block cancels and awaits this borrower before request/client/token disposal.
#pragma warning disable CA2025
        var sending = client.SendAsync(request, deadline.Token);
#pragma warning restore CA2025
        HttpResponseMessage? response = null;
        try
        {
            await Task.WhenAny(firstRead.Task, sending).WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.True(firstRead.Task.IsCompletedSuccessfully, "Upload settled before its first bytes reached the gated origin.");
            var serializedAtFirstRead = body.SerializedBytes;
            Assert.InRange(serializedAtFirstRead, 0, BodyBytes - 1);
            allowRead.TrySetResult();
            response = await sending.ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(expectedHash, await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(true), StringComparer.Ordinal);
            Assert.Equal(BodyBytes, body.SerializedBytes);
            Assert.Equal(1, Volatile.Read(ref originRequests));
            var memory = proxy.ReadMemory();
            output.WriteLine("large_stream_upload=" + JsonSerializer.Serialize(new
            {
                BodyBytes, ConfiguredCreditWindows = configuredCreditWindows, SerializedAtFirstRead = serializedAtFirstRead,
                body.SerializedBytes, OriginRequests = originRequests, proxy.EvidenceId,
                memory.Application, memory.Gateway,
                Qualification = "One repeating32KiB HTTP1 upload; raw process counters only. External owned-cgroup evidence is required for an aggregate memory budget claim.",
            }));
        }
        finally
        {
            allowRead.TrySetResult();
            await deadline.CancelAsync().ConfigureAwait(true);
            try { response ??= await sending.ConfigureAwait(true); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            response?.Dispose();
        }
    }

    private static Task<DevelopmentHttpUpstream> CreateOriginAsync(string expectedHash, TaskCompletionSource firstRead,
        TaskCompletionSource allowRead, Action observeRequest) =>
        DevelopmentHttpUpstream.StartAsync(async context =>
        {
            Assert.Equal(BodyBytes, context.Request.ContentLength);
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()
                ?? throw new InvalidOperationException("Origin request-body admission feature is absent.");
            limit.MaxRequestBodySize = BodyBytes;
            observeRequest();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[32 * 1024];
            long received = 0;
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false)) != 0)
            {
                received += count;
                hash.AppendData(buffer.AsSpan(0, count));
                if (received == count)
                {
                    firstRead.TrySetResult();
                    await allowRead.Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                }
            }
            Assert.Equal(BodyBytes, received);
            Assert.Equal(expectedHash, Convert.ToHexString(hash.GetHashAndReset()), StringComparer.Ordinal);
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync(expectedHash, context.RequestAborted).ConfigureAwait(false);
        });
}
