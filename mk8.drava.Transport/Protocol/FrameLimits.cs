using System.Text;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Protocol;

public static class FrameLimits
{
    public const uint Version = 1;
    public const int MaximumFrameBytes = 32 * 1024;
    public const int MaximumHeaderBytes = 32 * 1024;
    public const int MaximumHeaderCount = 128;
    public const int MaximumTargetBytes = 8192;
    public const int MaximumResetBytes = 256;

    public static void ValidateRequest(RequestHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.Version != Version || !Guid.TryParseExact(head.ExchangeId, "N", out _)) throw new InvalidDataException("Unsupported exchange version or identity.");
        if (head.StreamWindowFrames > 8) throw new InvalidDataException("Invalid exchange credit window.");
        if (head.GatewayId.Length is < 1 or > 128 || head.ListenerId.Length is < 1 or > 128) throw new InvalidDataException("Missing presentation identity.");
        if (head.Method.Length is < 1 or > 32 || !head.Method.All(IsToken)) throw new InvalidDataException("Invalid HTTP method.");
        if (head.RawTarget.Length == 0 || Encoding.UTF8.GetByteCount(head.RawTarget) > MaximumTargetBytes || head.RawTarget.Any(static value => value <= ' ' || value > '~')) throw new InvalidDataException("Invalid raw request target.");
        if (!head.RawTarget.StartsWith('/') && !string.Equals(head.RawTarget, "*", StringComparison.Ordinal)) throw new InvalidDataException("Only origin-form request targets are accepted.");
        if (head.Authority.Length is < 1 or > 512 || head.Authority.Any(static value => value <= ' ' || value > '~' || value is '/' or '\\' or '@' or ',')) throw new InvalidDataException("Invalid request authority.");
        if (head.Scheme is not "http" and not "https" || head.ClientProtocol is not "HTTP/1.1" and not "HTTP/2" and not "HTTP/3") throw new InvalidDataException("Unsupported client protocol facts.");
        if (!System.Net.IPAddress.TryParse(head.PeerAddress, out _) || head.PeerPort > 65535) throw new InvalidDataException("Invalid peer facts.");
        if (head.HasContentLength && head.ContentLength < 0) throw new InvalidDataException("Negative request length.");
        if (!head.HasBody && head.HasContentLength && head.ContentLength != 0) throw new InvalidDataException("Inconsistent body facts.");
        ValidateHeaders(head.Headers, trailers: false);
        ValidateRequestFraming(head);
    }

    private static void ValidateRequestFraming(RequestHead head)
    {
        var hostCount = 0;
        var lengthCount = 0;
        var transferCount = 0;
        foreach (var field in head.Headers)
        {
            if (string.Equals(field.Name, "host", StringComparison.OrdinalIgnoreCase)
                && (++hostCount > 1 || !string.Equals(field.Value, head.Authority, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Request authority conflicts with its Host field.");
            if (string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase)
                && (++lengthCount > 1 || !head.HasContentLength || !long.TryParse(field.Value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var length) || length != head.ContentLength))
                throw new InvalidDataException("Request length metadata conflicts with its header fields.");
            if (string.Equals(field.Name, "transfer-encoding", StringComparison.OrdinalIgnoreCase)
                && (++transferCount > 1 || head.HasContentLength || !head.HasBody || !string.Equals(head.ClientProtocol, "HTTP/1.1", StringComparison.Ordinal)
                    || !string.Equals(field.Value, "chunked", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported or ambiguous transfer coding.");
        }
    }

    public static void ValidateResponse(ResponseHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.StatusCode is < 100 or > 599 || head.Informational != (head.StatusCode < 200 && head.StatusCode != 101) || head.Upgrade != (head.StatusCode == 101)) throw new InvalidDataException("Invalid response status facts.");
        ValidateHeaders(head.Headers, trailers: false);
    }

    public static void ValidateHeaders(IEnumerable<Header> headers, bool trailers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var count = 0;
        var bytes = 0;
        foreach (var header in headers)
        {
            if (++count > MaximumHeaderCount || header.Name.Length is < 1 or > 256 || !header.Name.All(IsToken) || header.Value.Any(static value => value is '\r' or '\n' or '\0' || (value < ' ' && value != '\t'))) throw new InvalidDataException("Invalid header fields.");
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(header.Name) + Encoding.UTF8.GetByteCount(header.Value));
            if (bytes > MaximumHeaderBytes) throw new InvalidDataException("Header byte limit exceeded.");
            if (trailers && (header.Name.Equals("content-length", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("host", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("connection", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("trailer", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("authorization", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("proxy-authorization", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Forbidden trailer field.");
        }
    }

    public static void ValidateData(DataFrame data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Payload.Length is < 1 or > MaximumFrameBytes) throw new InvalidDataException("Invalid data frame size.");
    }

    private static bool IsToken(char value) => char.IsAsciiLetterOrDigit(value) || value is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
