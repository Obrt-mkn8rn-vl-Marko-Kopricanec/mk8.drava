using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class NativeReferenceDifferentialProcessTests
{
    private const string RawTarget = "/items/a%2Fb?sig=a%2Bb%2Fc%3D";

    [Theory]
    [InlineData("GET", 200, true)]
    [InlineData("GET", 200, false)]
    [InlineData("HEAD", 200, true)]
    [InlineData("GET", 204, false)]
    [InlineData("GET", 304, false)]
    [InlineData("PUT", 201, true)]
    [InlineData("DELETE", 202, false)]
    public async Task NativeAndSplitIngressPreserveTheSameRequestAndResponseSemanticsAsync(string method, int status, bool knownLength)
    {
        ArgumentNullException.ThrowIfNull(method);
        var observations = new ConcurrentQueue<string>();
        var origin = await DevelopmentHttpUpstream.StartAsync(context => RespondAsync(context, status, knownLength, observations)).ConfigureAwait(true);
        await using var originLifetime = origin.ConfigureAwait(true);
        var native = await DevelopmentNativeProxy.StartAsync(origin.Port).ConfigureAwait(true);
        await using var nativeLifetime = native.ConfigureAwait(true);
        var split = await TwoProcessProxy.StartAsync(origin.Port, "svc.site.test", enrolledSite: true).ConfigureAwait(true);
        await using var splitLifetime = split.ConfigureAwait(true);
        using var nativeClient = new HttpClient { BaseAddress = new UriBuilder("http", IPAddress.Loopback.ToString(), native.Port).Uri, Timeout = TimeSpan.FromSeconds(10) };
        using var splitClient = new DevelopmentSiteClient(split.RootCertificatePath, split.TlsPort, "svc.site.test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var reference = await SendAsync(nativeClient, method, HttpVersion.Version11, deadline.Token).ConfigureAwait(true);
        Assert.Equal(status, reference.Status);
        foreach (var protocol in new[] { HttpVersion.Version11, HttpVersion.Version20 })
        {
            var actual = await SendAsync(splitClient.Client, method, protocol, deadline.Token).ConfigureAwait(true);
            Assert.Equal(reference.Status, actual.Status);
            Assert.Equal(reference.Body, actual.Body);
            Assert.Equal(reference.ContentLength, actual.ContentLength);
            Assert.Equal(reference.ContentType, actual.ContentType);
            Assert.Equal(reference.Etag, actual.Etag);
            Assert.Equal(reference.Cookies, actual.Cookies);
        }
        var requests = observations.ToArray();
        Assert.Equal(3, requests.Length); // Each mutation reaches the origin once per ingress.
        Assert.Equal(requests[0], requests[1]);
        Assert.Equal(requests[0], requests[2]);
    }

    private static async Task<ResponseObservation> SendAsync(HttpClient client, string method, Version protocol, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(RawTarget, UriKind.Relative))
        {
            Version = protocol, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Host = "svc.site.test";
        request.Headers.Add("Authorization", "SharedKey synthetic:development-signature");
        request.Headers.Add("x-ms-version", "2026-06-06");
        request.Headers.IfNoneMatch.ParseAdd("*");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(3, 9);
        if (string.Equals(method, "PUT", StringComparison.Ordinal)) request.Content = new ByteArrayContent("finite signed payload"u8.ToArray());
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(true);
        Assert.Equal(protocol, response.Version);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
        return new ResponseObservation((int)response.StatusCode, body, response.Content.Headers.ContentLength,
            response.Content.Headers.ContentType?.ToString(), response.Headers.ETag?.Tag, response.Headers.GetValues("Set-Cookie").ToArray());
    }

    private static async Task RespondAsync(HttpContext context, int status, bool knownLength, ConcurrentQueue<string> observations)
    {
        using var body = new MemoryStream();
        await context.Request.Body.CopyToAsync(body, context.RequestAborted).ConfigureAwait(false);
        observations.Enqueue(JsonSerializer.Serialize(new
        {
            context.Request.Method, RawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget,
            Host = context.Request.Host.Value, Authorization = context.Request.Headers.Authorization.ToString(),
            Version = context.Request.Headers["x-ms-version"].ToString(), Condition = context.Request.Headers.IfNoneMatch.ToString(),
            Range = context.Request.Headers.Range.ToString(), context.Request.ContentLength,
            Bytes = body.Length, Hash = Convert.ToHexString(SHA256.HashData(body.GetBuffer().AsSpan(0, checked((int)body.Length)))),
        }));
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/octet-stream";
        context.Response.Headers.ETag = "\"reference-etag\"";
        context.Response.Headers.Append("Set-Cookie", "first=one; Path=/");
        context.Response.Headers.Append("Set-Cookie", "second=two; Path=/");
        if (status is 204 or 304) return;
        if (knownLength) context.Response.ContentLength = 11;
        if (HttpMethods.IsHead(context.Request.Method)) return;
        await context.Response.WriteAsync("origin-body", context.RequestAborted).ConfigureAwait(false);
    }

    private sealed record ResponseObservation(int Status, byte[] Body, long? ContentLength, string? ContentType, string? Etag, string[] Cookies);
}
