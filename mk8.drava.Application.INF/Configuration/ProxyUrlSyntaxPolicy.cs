using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.INF.Configuration;
public sealed class ProxyUrlSyntaxPolicy : IProxyUrlSyntaxPolicy
{
    public bool IsAbsoluteUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out _);
    }

    public bool IsAbsoluteHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
