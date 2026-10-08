namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyForwardedHeadersOptions
{
    public bool Enabled { get; init; } = true;
    public IList<string> TrustedProxies { get; init; } = [];
}
