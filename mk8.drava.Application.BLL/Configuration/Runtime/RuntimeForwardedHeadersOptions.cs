namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeForwardedHeadersOptions
{
    public RuntimeForwardedHeadersOptions(bool Enabled, IReadOnlyList<string> TrustedProxies)
    {
        this.Enabled = Enabled;
        this.TrustedProxies = RuntimeList.Copy(TrustedProxies);
    }

    public bool Enabled { get; }
    public IReadOnlyList<string> TrustedProxies { get; }
}
