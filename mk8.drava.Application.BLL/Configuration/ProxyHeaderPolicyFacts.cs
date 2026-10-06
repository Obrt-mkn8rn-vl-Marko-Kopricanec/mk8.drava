namespace Mk8.Drava.Application.BLL.Configuration;
internal static class ProxyHeaderPolicyFacts
{
    private static readonly HashSet<string> RestrictedHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Content-Length",
        "Host",
        "X-Request-Id"
    };
    public static bool IsValidHttpFieldName(string? headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return false;
        }

        foreach (var character in headerName)
        {
            var valid = character is >= '!' and <= '~' && character is not '(' and not ')' and not '<' and not '>' and not '@' && character is not ',' and not ';' and not ':' and not '\\' and not '"' && character is not '/' and not '[' and not ']' and not '?' and not '=' && character is not '{' and not '}';
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsRestrictedHeaderName(string headerName)
    {
        return RestrictedHeaderNames.Contains(headerName);
    }

    public static void ValidatePolicyHeaderName(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        if (IsRestrictedHeaderName(headerName))
        {
            throw new ArgumentException("Header policy cannot modify restricted header names.", nameof(headerName));
        }

        if (!IsValidHttpFieldName(headerName))
        {
            throw new ArgumentException("Header policy header name must be a valid HTTP field name.", nameof(headerName));
        }
    }

    public static void ValidatePolicyHeaderValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(static character => character is '\r' or '\n'))
        {
            throw new ArgumentException("Header policy value must not contain CR or LF.", nameof(value));
        }
    }

    public static void ValidateSetHeader(string name, string value)
    {
        ValidatePolicyHeaderName(name);
        ValidatePolicyHeaderValue(value);
    }
}
