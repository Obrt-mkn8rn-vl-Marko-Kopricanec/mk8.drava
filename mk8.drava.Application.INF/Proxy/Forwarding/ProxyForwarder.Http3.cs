using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.INF.Proxy.Http3;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    private async ValueTask<ResponseForwardingResult> ForwardHttp3ResponseAsync(Http3UpstreamConnection upstreamHttp3,
        Stream clientStream, Http1RequestHead requestHead, RuntimeRoute route, RuntimeListener listener, RuntimeTimeouts timeouts,
        string upstreamTarget, bool preferClientKeepAlive, string requestId, bool suppressRetryableStatusResponse,
        Action markResponseStarted, CancellationToken cancellationToken)
    {
        var upstreamResponse = await upstreamHttp3.ReadResponseHeadAsync(listener.MaxResponseHeadBytes, timeouts,
            async (early, token) =>
            {
                var head = new Http1ResponseHead("HTTP/1.1", early.StatusCode, "Informational", Http1ResponseFraming.None, early.Headers);
                await WriteResponseHeadAsync(clientStream, head, BuildResponseHeaders(head, route), timeouts,
                    preferClientKeepAlive, requestId, listener, token).ConfigureAwait(false);
                markResponseStarted();
            }, cancellationToken).ConfigureAwait(false);
        var responseHeadTranslation = FramedUpstreamResponsePolicy.BuildHttp1ResponseHead(requestHead, new FramedUpstreamResponseTranslationInput(upstreamResponse.StatusCode, upstreamResponse.Headers, ResponseEndedWithHead: false));
        var responseHead = responseHeadTranslation switch
        {
            FramedUpstreamResponseTranslationResult.AcceptedResult accepted => accepted.ResponseHead,
            FramedUpstreamResponseTranslationResult.RejectedResult rejected => throw new Http3UpstreamProtocolException($"Upstream HTTP/3 response framing was invalid: {rejected.Reason}."),
            _ => throw new InvalidOperationException($"Unexpected upstream response translation result {responseHeadTranslation.GetType().Name}.")};
        if (ProxyRetryPolicy.ShouldSuppressRetryableStatusResponse(ProxyRetryRuntimeMapper.ToOutcomeInput(route.Retry), responseHead.StatusCode, suppressRetryableStatusResponse))
        {
            return CreateRetrySuppressedResult(responseHead.StatusCode);
        }

        var keepClientConnectionOpen = preferClientKeepAlive;
        var responseHeaders = BuildResponseHeaders(responseHead, route);
        if (ProxyCacheEligibilityPolicy.EvaluateResponseForBuffering(ProxyCacheRuntimeMapper.ToPolicyFacts(route.Cache), requestHead, responseHead) is ProxyCacheEligibilityResult.AcceptedResult)
        {
            var body = await ReadFramedUpstreamCacheCandidateBodyAsync((readTimeouts, token) => ReadHttp3DataChunkAsync(upstreamHttp3, readTimeouts, token), responseHead, endStream: false, route.Cache.MaxEntryBytes, timeouts, cancellationToken).ConfigureAwait(false);
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
            await RelayHttp3ResponseBodyAsync(upstreamHttp3, clientStream, responseHead, timeouts, cancellationToken).ConfigureAwait(false);
        }

        return new ResponseForwardingResult(true, keepClientConnectionOpen, false, responseHead.StatusCode);
    }

}
