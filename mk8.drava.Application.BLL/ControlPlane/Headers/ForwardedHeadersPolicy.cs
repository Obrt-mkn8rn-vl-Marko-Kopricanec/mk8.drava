using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using System.Globalization;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public sealed class ForwardedHeadersPolicy
{
    private static readonly string[] ForwardedHeaderNames = ["X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Port", "Forwarded"];
    private readonly IForwardedHeadersAddressPolicy _addressPolicy;
    public ForwardedHeadersPolicy(IForwardedHeadersAddressPolicy addressPolicy)
    {
        _addressPolicy = addressPolicy;
    }

    public ForwardedHeadersContext Build(Http1RequestHead requestHead, ForwardedHeadersListener listener, RuntimeForwardedHeadersOptions options, ForwardedHeadersPeer remotePeer)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(remotePeer);
        ArgumentNullException.ThrowIfNull(requestHead);
        ArgumentNullException.ThrowIfNull(options);
        var remoteAddress = remotePeer.Address;
        if (!options.Enabled)
        {
            return new ForwardedHeadersContext(remoteAddress, remoteAddress ?? remotePeer.Endpoint, []);
        }

        var immediatePeerTrusted = remoteAddress is not null && _addressPolicy.IsTrustedPeer(remoteAddress, options.TrustedProxies);
        var incomingFor = immediatePeerTrusted ? SplitHeaderValues(requestHead.Headers, "X-Forwarded-For") : [];
        var forwardedForNormalization = _addressPolicy.NormalizeForwardedFor(incomingFor);
        var resolvedClientAddress = forwardedForNormalization is ForwardedForNormalizationResult.NormalizedResult normalizedForwardedFor ? normalizedForwardedFor.ClientAddress : remoteAddress;
        List<string> forwardedFor = [];
        if (immediatePeerTrusted)
        {
            forwardedFor.AddRange(incomingFor);
        }

        if (remoteAddress is not null)
        {
            forwardedFor.Add(remoteAddress);
        }

        var forwardedHost = immediatePeerTrusted ? FirstHeaderValue(requestHead.Headers, "X-Forwarded-Host") ?? requestHead.Host : requestHead.Host;
        var forwardedProto = immediatePeerTrusted ? FirstHeaderValue(requestHead.Headers, "X-Forwarded-Proto") ?? listener.Scheme : listener.Scheme;
        var forwardedPort = immediatePeerTrusted ? FirstHeaderValue(requestHead.Headers, "X-Forwarded-Port") ?? listener.Port.ToString(CultureInfo.InvariantCulture) : listener.Port.ToString(CultureInfo.InvariantCulture);
        var headers = new List<ProxyHeaderField>
        {
            new("X-Forwarded-For", string.Join(", ", forwardedFor)),
            new("X-Forwarded-Host", forwardedHost),
            new("X-Forwarded-Proto", forwardedProto),
            new("X-Forwarded-Port", forwardedPort)
        };
        var standardFor = resolvedClientAddress ?? "unknown";
        headers.Add(new ProxyHeaderField("Forwarded", $"for={QuoteForwardedValue(FormatForwardedFor(standardFor))};proto={QuoteForwardedValue(forwardedProto)};host={QuoteForwardedValue(forwardedHost)}"));
        return new ForwardedHeadersContext(resolvedClientAddress, resolvedClientAddress ?? remotePeer.Endpoint, headers);
    }

    public static bool IsForwardedHeader(string headerName)
    {
        return ForwardedHeaderNames.Any(name => string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SplitHeaderValues(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        List<string> values = [];
        foreach (var header in headers)
        {
            if (!string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in header.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ContainsControl(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static string? FirstHeaderValue(IReadOnlyList<ProxyHeaderField> headers, string name)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(header.Value) && !ContainsControl(header.Value))
            {
                return header.Value.Trim();
            }
        }

        return null;
    }

    private static bool ContainsControl(string value)
    {
        return value.Any(char.IsControl);
    }

    private static string FormatForwardedFor(string value)
    {
        return value.Contains(":", StringComparison.Ordinal) && !value.StartsWith('[') ? $"[{value}]" : value;
    }

    private static string QuoteForwardedValue(string value)
    {
        var sanitized = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{sanitized}\"";
    }
}
