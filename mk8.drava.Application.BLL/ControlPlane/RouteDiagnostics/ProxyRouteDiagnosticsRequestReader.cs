using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public static partial class ProxyRouteDiagnosticsRequestReader
{
    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
        "Set-Cookie",
        "X-MDRAVA-Admin-Key"
    };
    private static List<ProxyHeaderField> BuildHeaders(RouteMatchDryRunRequest request, List<RouteMatchDryRunFinding> findings)
    {
        List<ProxyHeaderField> headers = [new("Host", request.Host.Trim())];
        foreach (var header in request.Headers.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var name = header.Key?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name) || ContainsControl(name) || name.Contains(':', StringComparison.Ordinal))
            {
                findings.Add(new RouteMatchDryRunFinding("warning", "header_ignored", "A submitted header name was invalid and ignored."));
                continue;
            }

            var value = header.Value ?? "";
            if (ContainsControl(value))
            {
                findings.Add(new RouteMatchDryRunFinding("warning", "header_ignored", "A submitted header value contained control characters and was ignored."));
                continue;
            }

            if (SensitiveHeaderNames.Contains(name))
            {
                findings.Add(new RouteMatchDryRunFinding("info", "sensitive_header_redacted", $"Header '{name}' was considered for policy decisions but is not echoed in diagnostics."));
                value = "redacted";
            }

            headers.Add(new ProxyHeaderField(name, value));
        }

        return headers;
    }

    private static ProxyRouteDiagnosticsRequestFraming ResolveFraming(IReadOnlyList<ProxyHeaderField> headers)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && header.Value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                return ProxyRouteDiagnosticsRequestFraming.Chunked;
            }
        }

        foreach (var header in headers)
        {
            if (string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase) && long.TryParse(header.Value.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var contentLength) && contentLength >= 0)
            {
                return ProxyRouteDiagnosticsRequestFraming.FromContentLength(contentLength);
            }
        }

        return ProxyRouteDiagnosticsRequestFraming.None;
    }

    private static bool IsUpgrade(IReadOnlyList<ProxyHeaderField> headers)
    {
        return headers.Any(static header => string.Equals(header.Name, "Upgrade", StringComparison.OrdinalIgnoreCase));
    }

    private const int MaxInputLength = 4096;
    public static ProxyRouteDiagnosticsRequestDecision Read(RouteMatchDryRunRequest? request, DateTimeOffset evaluatedAtUtc, IProxyClientAddressSyntaxPolicy clientAddressSyntaxPolicy)
    {
        ArgumentNullException.ThrowIfNull(clientAddressSyntaxPolicy);
        if (request is null)
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "missing_request", "A dry-run request body is required."));
        }

        var scheme = NormalizeScheme(request.Scheme);
        if (scheme is not "http" and not "https")
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_scheme", "Scheme must be 'http' or 'https'."));
        }

        var protocol = NormalizeProtocol(request.Protocol);
        if (protocol is not null and not "http1" and not "http2" and not "http3")
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_protocol", "Protocol must be 'http1', 'http2', or 'http3' when supplied."));
        }

        if (string.Equals(protocol, "http3", StringComparison.Ordinal) && !string.Equals(scheme, "https", StringComparison.Ordinal))
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_protocol", "HTTP/3 dry-runs must use the https scheme."));
        }

        if (string.IsNullOrWhiteSpace(request.Host) || request.Host.Length > 253 || ContainsControl(request.Host))
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_host", "Host is required and must not contain control characters."));
        }

        if (request.Port is < 1 or > 65535)
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_port", "Port must be between 1 and 65535 when supplied."));
        }

        return ReadTarget(request, evaluatedAtUtc, clientAddressSyntaxPolicy, scheme, protocol);
    }

    private static ProxyRouteDiagnosticsRequestDecision ReadTarget(RouteMatchDryRunRequest request, DateTimeOffset evaluatedAtUtc, IProxyClientAddressSyntaxPolicy clientAddressSyntaxPolicy, string scheme, string? protocol)
    {
        var method = NormalizeMethod(request.Method);
        if (method.Length is 0 or > 32 || ContainsControl(method) || method.Any(static character => char.IsWhiteSpace(character)))
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_method", "Method must be a non-empty HTTP token."));
        }

        var path = NormalizePath(request.Path);
        if (path.Length is 0 or > MaxInputLength || !path.StartsWith('/'))
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_path", "Path must start with '/' and stay within the dry-run size limit."));
        }

        if (ContainsControl(path))
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_path", "Path must not contain control characters."));
        }

        var query = NormalizeQuery(request.Query);
        if (query.Length > MaxInputLength || ContainsControl(query))
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_query", "Query must stay within the dry-run size limit and must not contain control characters."));
        }

        if (!string.IsNullOrWhiteSpace(request.ClientIp) && !clientAddressSyntaxPolicy.IsIpLiteral(request.ClientIp))
        {
            return ProxyRouteDiagnosticsRequestDecision.Rejected(Failure(evaluatedAtUtc, "invalid_client_ip", "ClientIp must be an IPv4 or IPv6 literal when supplied."));
        }

        var findings = new List<RouteMatchDryRunFinding>();
        var target = path + query;
        var headers = BuildHeaders(request, findings);
        var requestHead = new ProxyRouteDiagnosticsRequestHead(method, target, path, "HTTP/1.1", request.Host.Trim(), ResolveFraming(headers), headers);
        return ProxyRouteDiagnosticsRequestDecision.Accepted(new ProxyRouteDiagnosticsRequestInput(scheme, protocol, request.ListenerName, request.Port, target, path, requestHead, IsUpgrade(headers), findings));
    }

    private static RouteMatchDryRunResult.FailedResult Failure(DateTimeOffset evaluatedAtUtc, string reason, string message)
    {
        return RouteMatchDryRunResult.Failed(evaluatedAtUtc, reason, message);
    }

    private static string NormalizeScheme(string value)
    {
        #pragma warning disable CA1308 // The accepted dry-run scheme/protocol vocabulary and projections are canonical lower-case; invalid input must retain existing rejection semantics.
        return string.IsNullOrWhiteSpace(value) ? "http" : value.Trim().ToLowerInvariant();
        #pragma warning restore CA1308
    }

    private static string NormalizeMethod(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "GET" : value.Trim().ToUpperInvariant();
    }

    private static string NormalizePath(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "/" : value.Trim();
    }

    private static string NormalizeQuery(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var query = value.Trim();
        return query.StartsWith('?') ? query : "?" + query;
    }

    private static string? NormalizeProtocol(string? value)
    {
        #pragma warning disable CA1308 // The accepted dry-run scheme/protocol vocabulary and projections are canonical lower-case; invalid input must retain existing rejection semantics.
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        #pragma warning restore CA1308
    }

    private static bool ContainsControl(string value)
    {
        return value.Any(char.IsControl);
    }
}
