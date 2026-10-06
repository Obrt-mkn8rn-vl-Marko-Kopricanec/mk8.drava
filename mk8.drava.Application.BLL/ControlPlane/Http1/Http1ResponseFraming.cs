namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public sealed record Http1ResponseFraming
{
    public Http1ResponseFraming(Http1BodyKind Kind, long? ContentLength)
    {
        Http1FramingFacts.ValidateResponse(Kind, ContentLength);
        this.Kind = Kind;
        this.ContentLength = ContentLength;
    }

    public Http1BodyKind Kind { get; }
    public long? ContentLength { get; }
    public static Http1ResponseFraming None { get; } = new(Http1BodyKind.None, null);

    public static Http1ResponseFraming FromContentLength(long contentLength)
    {
        return contentLength == 0 ? None : new Http1ResponseFraming(Http1BodyKind.ContentLength, contentLength);
    }

    public static Http1ResponseFraming Chunked { get; } = new(Http1BodyKind.Chunked, null);
    public static Http1ResponseFraming CloseDelimited { get; } = new(Http1BodyKind.CloseDelimited, null);
}
