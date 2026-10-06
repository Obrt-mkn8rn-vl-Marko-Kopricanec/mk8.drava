using System.Net;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.INF.Configuration;
public sealed class ProxyAdminUrlPolicy : IProxyAdminUrlPolicy
{
    public bool IsValid(string url)
    {
        return TryCreateAbsoluteUri(url, out _);
    }

    public bool IsLocal(string url)
    {
        if (!TryCreateAbsoluteUri(url, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(uri.Host, "*", StringComparison.Ordinal) || string.Equals(uri.Host, "+", StringComparison.Ordinal))
        {
            return false;
        }

        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    public bool IsNonLocal(string url)
    {
        return IsValid(url) && !IsLocal(url);
    }

    private static bool TryCreateAbsoluteUri(string url, out Uri uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri!))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
