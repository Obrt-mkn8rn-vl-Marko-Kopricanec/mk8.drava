using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public static class FramedUpstreamResponsePolicy
{
    public static FramedUpstreamResponseTranslationResult BuildHttp1ResponseHead(Http1RequestHead requestHead, FramedUpstreamResponseTranslationInput upstreamResponse)
    {
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(upstreamResponse);
        var framingDecision = DetermineFraming(requestHead, upstreamResponse);
        if (framingDecision is not UpstreamResponseFramingDecision.Accepted acceptedFraming)
        {
            return FramedUpstreamResponseTranslationResult.Rejected(((UpstreamResponseFramingDecision.Rejected)framingDecision).Reason);
        }

        return FramedUpstreamResponseTranslationResult.Accepted(new Http1ResponseHead("HTTP/1.1", upstreamResponse.StatusCode, ProxyRouteActionPolicy.ReasonPhrase(upstreamResponse.StatusCode), acceptedFraming.Framing, upstreamResponse.Headers, string.Equals(requestHead.Method, "HEAD", StringComparison.OrdinalIgnoreCase)));
    }

    private static UpstreamResponseFramingDecision DetermineFraming(Http1RequestHead requestHead, FramedUpstreamResponseTranslationInput upstreamResponse)
    {
        var contentLengthValues = upstreamResponse.Headers.Where(header => string.Equals(header.Name, "content-length", StringComparison.OrdinalIgnoreCase)).Select(header => header.Value).ToArray();
        long? contentLength = null;
        if (contentLengthValues.Length > 0)
        {
            var contentLengthAnalysis = Http1RequestParser.AnalyzeContentLength(contentLengthValues);
            if (contentLengthAnalysis is Http1ContentLengthAnalysisResult.Rejected rejectedContentLength)
                return UpstreamResponseFramingDecision.Reject(Http1ParseErrorText.FromError(rejectedContentLength.Error));
            contentLength = ((Http1ContentLengthAnalysisResult.Accepted)contentLengthAnalysis).ContentLength;
            if (upstreamResponse.StatusCode is >= 100 and < 200 or 204)
                return UpstreamResponseFramingDecision.Reject("Content-Length is forbidden for this response status");
        }

        if (string.Equals(requestHead.Method, "HEAD", StringComparison.OrdinalIgnoreCase) || upstreamResponse.StatusCode is 204 or 304)
            return UpstreamResponseFramingDecision.Accept(Http1ResponseFraming.None);
        if (upstreamResponse.ResponseEndedWithHead)
            return contentLength is > 0
                ? UpstreamResponseFramingDecision.Reject("Response ended before its declared content length")
                : UpstreamResponseFramingDecision.Accept(Http1ResponseFraming.None);
        return UpstreamResponseFramingDecision.Accept(contentLength is { } length
            ? Http1ResponseFraming.FromContentLength(length) : Http1ResponseFraming.Chunked);
    }

    private abstract record UpstreamResponseFramingDecision
    {
        private UpstreamResponseFramingDecision()
        {
        }

        public static Accepted Accept(Http1ResponseFraming framing)
        {
            return new Accepted(framing);
        }

        public static Rejected Reject(string reason)
        {
            return new Rejected(reason);
        }

        public sealed record Accepted : UpstreamResponseFramingDecision
        {
            public Accepted(Http1ResponseFraming framing)
            {
                Framing = framing;
            }

            public Http1ResponseFraming Framing { get; }
        }

        public sealed record Rejected : UpstreamResponseFramingDecision
        {
            public Rejected(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("Upstream response framing rejection reason is required.", nameof(reason));
                }

                Reason = reason;
            }

            public string Reason { get; }
        }
    }
}
