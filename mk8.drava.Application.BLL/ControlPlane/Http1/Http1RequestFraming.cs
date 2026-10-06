namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public sealed record Http1RequestFraming
{
    public Http1RequestFraming(Http1BodyKind Kind, long? ContentLength)
    {
        Http1FramingFacts.ValidateRequest(Kind, ContentLength);
        this.Kind = Kind;
        this.ContentLength = ContentLength;
    }

    public Http1BodyKind Kind { get; }
    public long? ContentLength { get; }
    public static Http1RequestFraming None { get; } = new(Http1BodyKind.None, null);

    public static Http1RequestFraming FromContentLength(long contentLength)
    {
        return contentLength == 0 ? None : new Http1RequestFraming(Http1BodyKind.ContentLength, contentLength);
    }

    public static Http1RequestFraming Chunked { get; } = new(Http1BodyKind.Chunked, null);
}
