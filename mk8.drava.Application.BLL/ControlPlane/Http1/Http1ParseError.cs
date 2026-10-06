namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public enum Http1ParseError
{
    None,
    EmptyRequest,
    InvalidRequestLine,
    UnsupportedVersion,
    InvalidTarget,
    TargetTooLarge,
    MissingHost,
    InvalidHeaderLine,
    HeaderLineTooLarge,
    HeaderCountExceeded,
    InvalidContentLength,
    ConflictingContentLength,
    InvalidTransferEncoding,
    ContentLengthWithTransferEncoding,
    UnsupportedTransferEncoding,
    InvalidStatusLine
}
