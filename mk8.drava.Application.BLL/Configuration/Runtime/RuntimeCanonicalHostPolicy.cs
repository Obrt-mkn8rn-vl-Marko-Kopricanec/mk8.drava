namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeCanonicalHostPolicy
{
    public RuntimeCanonicalHostPolicy(bool Enabled, string TargetHost, int StatusCode)
    {
        RuntimeRedirectFacts.ValidateCanonicalHost(Enabled, TargetHost, StatusCode);
        this.Enabled = Enabled;
        this.TargetHost = TargetHost;
        this.StatusCode = StatusCode;
    }

    public bool Enabled { get; }
    public string TargetHost { get; }
    public int StatusCode { get; }
}
