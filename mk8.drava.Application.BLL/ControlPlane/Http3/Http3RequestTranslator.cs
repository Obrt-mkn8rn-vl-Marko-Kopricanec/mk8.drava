using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.Http;
using System.Globalization;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class Http3RequestTranslator
{
    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection",
        "upgrade",
        "keep-alive",
        "proxy-connection",
        "transfer-encoding",
        "te"
    };
    public static Http3RequestTranslationResult BuildRequest(IReadOnlyList<ProxyHeaderField> headers, Http3RequestTranslationListenerInput listener, bool bodyMayFollow = true)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(headers);
        Dictionary<string, string> pseudo = new(StringComparer.Ordinal);
        List<ProxyHeaderField> regularHeaders = [];
        var regularHeaderSeen = false;
        foreach (var header in headers)
        {
            if (header.Name.Length == 0)
            {
                return Http3RequestTranslationResult.Rejected("empty_header_name");
            }

            if (header.Name.Any(static character => char.IsAsciiLetterUpper(character)))
            {
                return Http3RequestTranslationResult.Rejected("uppercase_header_name");
            }

            if (header.Name[0] == ':')
            {
                if (regularHeaderSeen || pseudo.ContainsKey(header.Name) || !IsAllowedPseudoHeader(header.Name))
                {
                    return Http3RequestTranslationResult.Rejected("invalid_pseudo_header");
                }

                pseudo[header.Name] = header.Value;
                continue;
            }

            regularHeaderSeen = true;
            if (!IsValidHeaderName(header.Name))
            {
                return Http3RequestTranslationResult.Rejected("invalid_header_name");
            }

            if (ForbiddenHeaders.Contains(header.Name))
            {
                return Http3RequestTranslationResult.Rejected("forbidden_header");
            }

            regularHeaders.Add(header);
        }

        if (!pseudo.TryGetValue(":method", out var method))
        {
            return Http3RequestTranslationResult.Rejected("missing_pseudo_header");
        }

        if (pseudo.ContainsKey(":protocol"))
        {
            return Http3RequestTranslationResult.Rejected("extended_connect_unsupported");
        }

        if (ProxyRequestMethodPolicy.IsConnectTunnelMethod(method))
        {
            return BuildConnectRequest(pseudo, regularHeaders, listener, method);
        }

        if (!pseudo.TryGetValue(":scheme", out var scheme) || !pseudo.TryGetValue(":authority", out var authority) || !pseudo.TryGetValue(":path", out var target))
        {
            return Http3RequestTranslationResult.Rejected("missing_pseudo_header");
        }

        if (!string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) || !listener.IsHttps)
        {
            return Http3RequestTranslationResult.Rejected("invalid_scheme");
        }

        if (!ProxyRequestMethodPolicy.IsValidMethodToken(method))
        {
            return Http3RequestTranslationResult.Rejected("invalid_method");
        }

        if (!IsValidAuthority(authority) || !IsValidTarget(target))
        {
            return Http3RequestTranslationResult.Rejected("invalid_target");
        }

        var hostHeader = regularHeaders.FirstOrDefault(static header => string.Equals(header.Name, "host", StringComparison.OrdinalIgnoreCase));
        if (hostHeader is not null && !string.Equals(hostHeader.Value, authority, StringComparison.OrdinalIgnoreCase))
        {
            return Http3RequestTranslationResult.Rejected("authority_host_mismatch");
        }

        var framingDecision = GetRequestFraming(regularHeaders, method, bodyMayFollow);
        if (framingDecision is Http3RequestFramingDecision.RejectedDecision rejectedFraming)
        {
            return Http3RequestTranslationResult.Rejected(rejectedFraming.Reason);
        }

        var framing = ((Http3RequestFramingDecision.AcceptedDecision)framingDecision).Framing;
        regularHeaders.RemoveAll(static header => string.Equals(header.Name, "host", StringComparison.OrdinalIgnoreCase));
        var path = target.Split('?', 2)[0];
        regularHeaders.Insert(0, new ProxyHeaderField("Host", authority));
        return Http3RequestTranslationResult.Accepted(new Http1RequestHead(method, target, path, "HTTP/3", authority, framing, regularHeaders));
    }

    private static bool IsAllowedPseudoHeader(string name)
    {
        return name is ":method" or ":scheme" or ":authority" or ":path" or ":protocol";
    }

    private static Http3RequestTranslationResult BuildConnectRequest(IReadOnlyDictionary<string, string> pseudo, List<ProxyHeaderField> regularHeaders, Http3RequestTranslationListenerInput listener, string method)
    {
        if (pseudo.ContainsKey(":scheme") || pseudo.ContainsKey(":path"))
        {
            return Http3RequestTranslationResult.Rejected("malformed_connect");
        }

        if (!pseudo.TryGetValue(":authority", out var authority) || !IsValidConnectAuthority(authority))
        {
            return Http3RequestTranslationResult.Rejected("invalid_connect_target");
        }

        if (!listener.IsHttps)
        {
            return Http3RequestTranslationResult.Rejected("invalid_scheme");
        }

        var hostHeader = regularHeaders.FirstOrDefault(static header => string.Equals(header.Name, "host", StringComparison.OrdinalIgnoreCase));
        if (hostHeader is not null && !string.Equals(hostHeader.Value, authority, StringComparison.OrdinalIgnoreCase))
        {
            return Http3RequestTranslationResult.Rejected("authority_host_mismatch");
        }

        var framingDecision = GetRequestFraming(regularHeaders, method, bodyMayFollow: false);
        if (framingDecision is Http3RequestFramingDecision.RejectedDecision rejectedFraming)
        {
            return Http3RequestTranslationResult.Rejected(rejectedFraming.Reason);
        }

        var framing = ((Http3RequestFramingDecision.AcceptedDecision)framingDecision).Framing;
        if (framing.Kind != Http1BodyKind.None)
        {
            return Http3RequestTranslationResult.Rejected("connect_body_unsupported");
        }

        regularHeaders.RemoveAll(static header => string.Equals(header.Name, "host", StringComparison.OrdinalIgnoreCase));
        regularHeaders.Insert(0, new ProxyHeaderField("Host", authority));
        return Http3RequestTranslationResult.Accepted(new Http1RequestHead(method, authority, authority, "HTTP/3", authority, Http1RequestFraming.None, regularHeaders));
    }

    private static bool IsValidAuthority(string authority)
    {
        return !string.IsNullOrWhiteSpace(authority) && authority.All(static character => character > 0x20 && character != 0x7f && character is not '/' and not '\\' and not '?' and not '#' and not '@');
    }

    private static bool IsValidConnectAuthority(string authority)
    {
        if (!IsValidAuthority(authority))
        {
            return false;
        }

        var portSeparator = authority.LastIndexOf(':');
        if (portSeparator <= 0 || portSeparator == authority.Length - 1)
        {
            return false;
        }

        if (authority[0] == '[')
        {
            var bracket = authority.IndexOf(']');
            if (bracket <= 1 || bracket + 1 != portSeparator)
            {
                return false;
            }
        }
        else if (authority.IndexOf(':') != portSeparator)
        {
            return false;
        }

        return int.TryParse(authority[(portSeparator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535;
    }

    private static bool IsValidTarget(string target)
    {
        return !string.IsNullOrWhiteSpace(target) && target.StartsWith('/') && target.All(static character => character > 0x20 && character != 0x7f && character != '#');
    }

    private static bool IsValidHeaderName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.All(static character => character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~' || character is >= '0' and <= '9' || character is >= 'a' and <= 'z');
    }

    private static Http3RequestFramingDecision GetRequestFraming(IReadOnlyList<ProxyHeaderField> headers, string method, bool bodyMayFollow)
    {
        long? declared = null;
        foreach (var header in headers)
        {
            if (!string.Equals(header.Name, "content-length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (declared.HasValue || !long.TryParse(header.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            {
                return Http3RequestFramingDecision.Rejected("invalid_content_length");
            }

            declared = parsed;
        }

        if (declared.HasValue)
        {
            return Http3RequestFramingDecision.Accepted(Http1RequestFraming.FromContentLength(declared.Value));
        }

        if (bodyMayFollow && MayCarryBody(method))
        {
            return Http3RequestFramingDecision.Accepted(Http1RequestFraming.Chunked);
        }

        return Http3RequestFramingDecision.Accepted(Http1RequestFraming.None);
    }

    private static bool MayCarryBody(string method)
    {
        return string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);
    }

    private abstract record Http3RequestFramingDecision
    {
        private Http3RequestFramingDecision()
        {
        }

        public static Http3RequestFramingDecision Accepted(Http1RequestFraming framing)
        {
            ArgumentNullException.ThrowIfNull(framing);
            return new AcceptedDecision(framing);
        }

        public static Http3RequestFramingDecision Rejected(string reason)
        {
            return new RejectedDecision(reason);
        }

        public sealed record AcceptedDecision(Http1RequestFraming Framing) : Http3RequestFramingDecision;
        public sealed record RejectedDecision : Http3RequestFramingDecision
        {
            public RejectedDecision(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new ArgumentException("HTTP/3 request framing rejection reason is required.", nameof(reason));
                }

                Reason = reason;
            }

            public string Reason { get; }
        }
    }
}
