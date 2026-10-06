using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public sealed class HopByHopHeaderPolicy
{
    private static readonly HashSet<string> StandardHopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Connection",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };
    public static bool IsHopByHopHeader(string name)
    {
        return StandardHopByHopHeaders.Contains(name);
    }

    public static bool HasConnectionToken(IReadOnlyList<ProxyHeaderField> headers, string token)
    {
        ArgumentNullException.ThrowIfNull(headers);
        foreach (var header in headers)
        {
            if (!string.Equals(header.Name, "Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in header.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(value, token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public IReadOnlyList<ProxyHeaderField> FilterForForwarding(IReadOnlyList<ProxyHeaderField> headers, bool preserveTransferEncoding, bool preserveTrailer)
    {
        ArgumentNullException.ThrowIfNull(headers);
        HashSet<string> nominated = new(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (!string.Equals(header.Name, "Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var token in header.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                nominated.Add(token);
            }
        }

        List<ProxyHeaderField> filtered = [];
        foreach (var header in headers)
        {
            if (nominated.Contains(header.Name))
            {
                continue;
            }

            if (StandardHopByHopHeaders.Contains(header.Name))
            {
                if (preserveTransferEncoding && string.Equals(header.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (preserveTrailer && string.Equals(header.Name, "Trailer", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(header);
                }

                continue;
            }

            filtered.Add(header);
        }

        return filtered;
    }
}
