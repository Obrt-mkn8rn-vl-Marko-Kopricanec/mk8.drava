using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.INF.Proxy.Http2;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    private async ValueTask<ResponseForwardingResult> ForwardHttp2ResponseAsync(Http2UpstreamConnection upstreamHttp2,
        Stream clientStream, Http1RequestHead requestHead, RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts,
        string upstreamTarget, bool preferClientKeepAlive, string requestId, bool suppressRetryableStatusResponse,
        Action markResponseStarted, Action<int>? onFinalHead, CancellationToken cancellationToken)
    {
        var upstreamResponse = await upstreamHttp2.ReadResponseHeadAsync(listener.MaxResponseHeadBytes, timeouts, cancellationToken).ConfigureAwait(false);
        var responseHeadTranslation = FramedUpstreamResponsePolicy.BuildHttp1ResponseHead(requestHead, new FramedUpstreamResponseTranslationInput(upstreamResponse.StatusCode, upstreamResponse.Headers, upstreamResponse.EndStream));
        var responseHead = responseHeadTranslation switch
        {
            FramedUpstreamResponseTranslationResult.AcceptedResult accepted => accepted.ResponseHead,
            FramedUpstreamResponseTranslationResult.RejectedResult rejected => throw new Http2UpstreamProtocolException($"Upstream HTTP/2 response framing was invalid: {rejected.Reason}."),
            _ => throw new InvalidOperationException($"Unexpected upstream response translation result {responseHeadTranslation.GetType().Name}.")};
        onFinalHead?.Invoke(responseHead.StatusCode);
        if (ProxyRetryPolicy.ShouldSuppressRetryableStatusResponse(ProxyRetryRuntimeMapper.ToOutcomeInput(route.Retry), responseHead.StatusCode, suppressRetryableStatusResponse))
        {
            return CreateRetrySuppressedResult(responseHead.StatusCode);
        }

        var keepClientConnectionOpen = preferClientKeepAlive;
        var responseHeaders = BuildResponseHeaders(responseHead, route);
        if (ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(ProxyCacheRuntimeMapper.ToPolicyFacts(route.Cache), requestHead, responseHead) is ProxyCacheEligibilityResult.AcceptedResult)
        {
            var body = await ReadFramedUpstreamCacheCandidateBodyAsync((readTimeouts, token) => ReadHttp2DataChunkAsync(upstreamHttp2, readTimeouts, token), responseHead, upstreamResponse.EndStream, timeouts, cancellationToken).ConfigureAwait(false);
            if (body.Trailers is { Count: > 0 })
            {
                _cacheStore.RecordUncacheable(ProxyCacheRuntimeMapper.ToPolicyFacts(route.Cache), "trailers");
                await WriteBufferedFramedTrailersAsync(clientStream, responseHead, responseHeaders, body, listener, timeouts,
                    requestId, keepClientConnectionOpen, markResponseStarted, cancellationToken).ConfigureAwait(false);
            }
            else
                await WriteAndStoreBufferedCacheResponseAsync(clientStream, route, listener, timeouts, requestHead, upstreamTarget,
                    responseHead, responseHeaders, body.Data, keepClientConnectionOpen, requestId, markResponseStarted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            RecordUncacheableFraming(route.Cache, responseHead);
            await WriteResponseHeadAsync(clientStream, responseHead, responseHeaders, timeouts, keepClientConnectionOpen, requestId, listener, cancellationToken).ConfigureAwait(false);
            markResponseStarted();
            await RelayHttp2ResponseBodyAsync(upstreamHttp2, clientStream, upstreamResponse.EndStream, responseHead, timeouts, cancellationToken).ConfigureAwait(false);
        }

        return new ResponseForwardingResult(true, keepClientConnectionOpen, false, responseHead.StatusCode);
    }

}
