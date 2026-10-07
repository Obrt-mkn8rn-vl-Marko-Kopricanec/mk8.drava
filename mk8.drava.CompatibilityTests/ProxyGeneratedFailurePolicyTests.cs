using System.Text;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyGeneratedFailurePolicyTests
{
    public static void AllowsGeneratedFailureOnlyBeforeResponseAndWithoutSuppression()
    {
        AssertEx.True(ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted: false, suppressGeneratedFailureResponse: false));
        AssertEx.False(ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted: true, suppressGeneratedFailureResponse: false));
        AssertEx.False(ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted: false, suppressGeneratedFailureResponse: true));
        AssertEx.False(ProxyGeneratedFailurePolicy.CanWriteFailureResponse(responseStarted: true, suppressGeneratedFailureResponse: true));
    }

    public static void BuildsGeneratedFailureResponseFromFailureResult()
    {
        var defaultFailure = ForwardingResult.Failure(responseStarted: false, responseStatusCode: null, failureKind: ProxyFailureKind.UpstreamConnectFailed);
        var timeoutFailure = ForwardingResult.Failure(responseStarted: false, responseStatusCode: 504, failureKind: ProxyFailureKind.UpstreamResponseHeadTimeout);
        var noHealthyFailure = ForwardingResult.Failure(responseStarted: false, responseStatusCode: null, failureKind: ProxyFailureKind.NoHealthyUpstream);
        var defaultResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse((ForwardingResult.FailureResult)defaultFailure);
        var timeoutResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse((ForwardingResult.FailureResult)timeoutFailure);
        var noHealthyResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse((ForwardingResult.FailureResult)noHealthyFailure);
        var clientBodyTimeoutResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse(ProxyFailureKind.ClientRequestBodyTimeout);
        var upgradeRejectedResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse(ProxyFailureKind.UpgradeRejected);
        AssertEx.Equal(502, defaultResponse.StatusCode);
        AssertEx.Equal("Bad Gateway", defaultResponse.ReasonPhrase);
        AssertEx.Equal("Bad Gateway", defaultResponse.Body);
        AssertEx.Equal(ProxyFailureKind.UpstreamConnectFailed, defaultResponse.FailureKind);
        AssertEx.Equal(504, timeoutResponse.StatusCode);
        AssertEx.Equal("Gateway Timeout", timeoutResponse.ReasonPhrase);
        AssertEx.Equal("Gateway Timeout", timeoutResponse.Body);
        AssertEx.Equal(ProxyFailureKind.UpstreamResponseHeadTimeout, timeoutResponse.FailureKind);
        AssertEx.Equal(503, noHealthyResponse.StatusCode);
        AssertEx.Equal("Service Unavailable", noHealthyResponse.ReasonPhrase);
        AssertEx.Equal("Service Unavailable", noHealthyResponse.Body);
        AssertEx.Equal(ProxyFailureKind.NoHealthyUpstream, noHealthyResponse.FailureKind);
        AssertEx.Equal(408, clientBodyTimeoutResponse.StatusCode);
        AssertEx.Equal("Request Timeout", clientBodyTimeoutResponse.ReasonPhrase);
        AssertEx.Equal("Request Timeout", clientBodyTimeoutResponse.Body);
        AssertEx.Equal(ProxyFailureKind.ClientRequestBodyTimeout, clientBodyTimeoutResponse.FailureKind);
        AssertEx.Equal(503, upgradeRejectedResponse.StatusCode);
        AssertEx.Equal("Service Unavailable", upgradeRejectedResponse.ReasonPhrase);
        AssertEx.Equal("Service Unavailable", upgradeRejectedResponse.Body);
        AssertEx.Equal(ProxyFailureKind.UpgradeRejected, upgradeRejectedResponse.FailureKind);
        var forwardingResult = (ForwardingResult.FailureResult)timeoutResponse.ToForwardingResult();
        AssertEx.True(forwardingResult.ResponseStarted);
        AssertEx.Equal(504, forwardingResult.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.UpstreamResponseHeadTimeout, forwardingResult.FailureKind);
        var noHealthyForwardingResult = (ForwardingResult.FailureResult)noHealthyResponse.ToForwardingResult();
        AssertEx.True(noHealthyForwardingResult.ResponseStarted);
        AssertEx.Equal(503, noHealthyForwardingResult.ResponseStatusCode);
        AssertEx.Equal(ProxyFailureKind.NoHealthyUpstream, noHealthyForwardingResult.FailureKind);
    }

    public static void ForwardingFailurePolicyHidesStatusAfterResponseStarts()
    {
        AssertEx.Equal(502, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted: false, ProxyFailureKind.UpstreamConnectFailed));
        AssertEx.Equal(413, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted: false, ProxyFailureKind.RequestPayloadTooLarge));
        AssertEx.Equal<int?>(null, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted: true, ProxyFailureKind.UpstreamConnectFailed));
        AssertEx.Equal<int?>(null, ProxyForwardingFailurePolicy.ResponseStatusCodeForFailure(responseStarted: true, ProxyFailureKind.RequestPayloadTooLarge));
    }

    public static void BuildsGeneratedFailureResponseWithExplicitBody()
    {
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(431, "Request Header Fields Too Large", "Request Head Too Large", ProxyFailureKind.ParserLimitExceeded);
        var sameBodyResponse = ProxyGeneratedFailurePolicy.BuildFailureResponse(502, "Bad Gateway", ProxyFailureKind.UpstreamConnectFailed);
        AssertEx.Equal(431, response.StatusCode);
        AssertEx.Equal("Request Header Fields Too Large", response.ReasonPhrase);
        AssertEx.Equal("Request Head Too Large", response.Body);
        AssertEx.Equal(ProxyFailureKind.ParserLimitExceeded, response.FailureKind);
        AssertEx.Equal(502, sameBodyResponse.StatusCode);
        AssertEx.Equal("Bad Gateway", sameBodyResponse.ReasonPhrase);
        AssertEx.Equal("Bad Gateway", sameBodyResponse.Body);
        AssertEx.Equal(ProxyFailureKind.UpstreamConnectFailed, sameBodyResponse.FailureKind);
    }

    public static void BuildsGeneratedFailureFramedHeaders()
    {
        var response = new ProxyGeneratedFailureResponse(502, "Bad Gateway", ProxyFailureKind.UpstreamConnectFailed);
        var headers = ProxyGeneratedFailurePolicy.BuildFramedResponseHeaders(response, "req-789", 11);
        AssertEx.Equal("text/plain", headers.Single(static header => string.Equals(header.Name, "content-type", StringComparison.Ordinal)).Value);
        AssertEx.Equal("req-789", headers.Single(static header => string.Equals(header.Name, "x-request-id", StringComparison.Ordinal)).Value);
        AssertEx.Equal("11", headers.Single(static header => string.Equals(header.Name, "content-length", StringComparison.Ordinal)).Value);
    }

    public static async Task GeneratedFailureWriterSerializesDescriptorBodyAsync()
    {
        var stream = new MemoryStream();
        await using var streamDisposal = stream.ConfigureAwait(false);
        var metrics = new ProxyMetrics();
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(431, "Request Header Fields Too Large", "Request Head Too Large", ProxyFailureKind.ParserLimitExceeded);
        await ProxyErrorResponses.WriteGeneratedFailureAsync(stream, response, "req-431", TimeSpan.FromSeconds(1), metrics, CancellationToken.None).ConfigureAwait(false);
        var text = Encoding.ASCII.GetString(stream.ToArray());
        AssertEx.True(text.StartsWith("HTTP/1.1 431 Request Header Fields Too Large\r\n", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("Connection: close\r\n", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("Content-Length: 22\r\n", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("Content-Type: text/plain\r\n", StringComparison.Ordinal), text);
        AssertEx.True(text.Contains("X-Request-Id: req-431\r\n", StringComparison.Ordinal), text);
        AssertEx.True(text.EndsWith("\r\n\r\nRequest Head Too Large", StringComparison.Ordinal), text);
        AssertEx.Equal(stream.Length, metrics.Snapshot().Traffic.BytesWritten);
    }
}
