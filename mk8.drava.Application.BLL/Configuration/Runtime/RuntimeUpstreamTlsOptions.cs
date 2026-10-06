namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeUpstreamTlsOptions
{
    public RuntimeUpstreamTlsOptions(bool ValidateCertificate, string? SniHost)
    {
        RuntimeUpstreamTlsFacts.Validate(SniHost);
        this.ValidateCertificate = ValidateCertificate;
        this.SniHost = SniHost;
    }

    public static RuntimeUpstreamTlsOptions Default { get; } = new(true, null);
    public bool ValidateCertificate { get; }
    public string? SniHost { get; }
}
