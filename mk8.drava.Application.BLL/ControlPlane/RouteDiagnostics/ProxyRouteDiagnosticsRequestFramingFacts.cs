namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
internal static class ProxyRouteDiagnosticsRequestFramingFacts
{
    public static void Validate(ProxyRouteDiagnosticsBodyKind kind, long? contentLength)
    {
        switch (kind)
        {
            case ProxyRouteDiagnosticsBodyKind.None:
            case ProxyRouteDiagnosticsBodyKind.Chunked:
                if (contentLength is not null)
                {
                    throw new ArgumentException("Route diagnostics request framing length is only valid for Content-Length bodies.", nameof(contentLength));
                }

                break;
            case ProxyRouteDiagnosticsBodyKind.ContentLength:
                if (contentLength is null or <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(contentLength));
                }

                break;
            case ProxyRouteDiagnosticsBodyKind.CloseDelimited:
                throw new ArgumentException("Route diagnostics request framing cannot be close-delimited.", nameof(kind));
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
