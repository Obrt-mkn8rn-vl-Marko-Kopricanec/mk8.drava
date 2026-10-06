namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
internal static class Http1FramingFacts
{
    public static void ValidateRequest(Http1BodyKind kind, long? contentLength)
    {
        if (kind == Http1BodyKind.CloseDelimited)
        {
            throw new ArgumentException("HTTP/1 request framing cannot be close-delimited.", nameof(kind));
        }

        Validate(kind, contentLength);
    }

    public static void ValidateResponse(Http1BodyKind kind, long? contentLength)
    {
        Validate(kind, contentLength);
    }

    private static void Validate(Http1BodyKind kind, long? contentLength)
    {
        switch (kind)
        {
            case Http1BodyKind.None:
            case Http1BodyKind.Chunked:
            case Http1BodyKind.CloseDelimited:
                if (contentLength is not null)
                {
                    throw new ArgumentException("HTTP/1 framing length is only valid for Content-Length bodies.", nameof(contentLength));
                }

                break;
            case Http1BodyKind.ContentLength:
                if (contentLength is null or <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(contentLength));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
