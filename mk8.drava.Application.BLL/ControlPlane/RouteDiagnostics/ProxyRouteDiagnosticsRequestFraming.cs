namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed record ProxyRouteDiagnosticsRequestFraming
{
    public ProxyRouteDiagnosticsRequestFraming(ProxyRouteDiagnosticsBodyKind Kind, long? ContentLength)
    {
        ProxyRouteDiagnosticsRequestFramingFacts.Validate(Kind, ContentLength);
        this.Kind = Kind;
        this.ContentLength = ContentLength;
    }

    public ProxyRouteDiagnosticsBodyKind Kind { get; }
    public long? ContentLength { get; }
    public static ProxyRouteDiagnosticsRequestFraming None { get; } = new(ProxyRouteDiagnosticsBodyKind.None, null);
    public static ProxyRouteDiagnosticsRequestFraming Chunked { get; } = new(ProxyRouteDiagnosticsBodyKind.Chunked, null);

    public static ProxyRouteDiagnosticsRequestFraming FromContentLength(long contentLength)
    {
        return contentLength == 0 ? None : new ProxyRouteDiagnosticsRequestFraming(ProxyRouteDiagnosticsBodyKind.ContentLength, contentLength);
    }
}
