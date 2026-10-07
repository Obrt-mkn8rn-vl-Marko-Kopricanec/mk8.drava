using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.INF.Proxy.Http2;

internal static class Http2ResponseFieldPolicy
{
    public static void Validate(ProxyHeaderField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Name.Length is < 1 or > 256 || !field.Name.All(IsLowercaseToken)
            || field.Value.Any(static value => value is '\r' or '\n' or '\0' or '\u007f' || (value < ' ' && value != '\t'))
            || field.Value.Length > 0 && (field.Value[0] is ' ' or '\t' || field.Value[^1] is ' ' or '\t'))
            throw new Http2UpstreamProtocolException("Malformed HTTP/2 response field.");
    }

    private static bool IsLowercaseToken(char value) => value is >= 'a' and <= 'z' or >= '0' and <= '9'
        or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
