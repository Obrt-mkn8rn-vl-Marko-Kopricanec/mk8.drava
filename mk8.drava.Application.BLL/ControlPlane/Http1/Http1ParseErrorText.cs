namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public static class Http1ParseErrorText
{
    public static string FromError(Http1ParseError error)
    {
        return error switch
        {
            Http1ParseError.None => nameof(Http1ParseError.None),
            Http1ParseError.EmptyRequest => nameof(Http1ParseError.EmptyRequest),
            Http1ParseError.InvalidRequestLine => nameof(Http1ParseError.InvalidRequestLine),
            Http1ParseError.UnsupportedVersion => nameof(Http1ParseError.UnsupportedVersion),
            Http1ParseError.InvalidTarget => nameof(Http1ParseError.InvalidTarget),
            Http1ParseError.TargetTooLarge => nameof(Http1ParseError.TargetTooLarge),
            Http1ParseError.MissingHost => nameof(Http1ParseError.MissingHost),
            Http1ParseError.InvalidHeaderLine => nameof(Http1ParseError.InvalidHeaderLine),
            Http1ParseError.HeaderLineTooLarge => nameof(Http1ParseError.HeaderLineTooLarge),
            Http1ParseError.HeaderCountExceeded => nameof(Http1ParseError.HeaderCountExceeded),
            Http1ParseError.InvalidContentLength => nameof(Http1ParseError.InvalidContentLength),
            Http1ParseError.ConflictingContentLength => nameof(Http1ParseError.ConflictingContentLength),
            Http1ParseError.InvalidTransferEncoding => nameof(Http1ParseError.InvalidTransferEncoding),
            Http1ParseError.ContentLengthWithTransferEncoding => nameof(Http1ParseError.ContentLengthWithTransferEncoding),
            Http1ParseError.UnsupportedTransferEncoding => nameof(Http1ParseError.UnsupportedTransferEncoding),
            Http1ParseError.InvalidStatusLine => nameof(Http1ParseError.InvalidStatusLine),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown HTTP/1 parse error.")};
    }
}
