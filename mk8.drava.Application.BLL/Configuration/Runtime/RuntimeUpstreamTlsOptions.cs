namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeUpstreamTlsOptions
{
    public RuntimeUpstreamTlsOptions(bool ValidateCertificate, string? SniHost) : this(ValidateCertificate, SniHost, null)
    {
    }

    public RuntimeUpstreamTlsOptions(bool ValidateCertificate, string? SniHost, RuntimeTrustedRootCertificate? TrustedRoot)
    {
        RuntimeUpstreamTlsFacts.Validate(SniHost);
        if (!ValidateCertificate && TrustedRoot is not null)
        {
            throw new ArgumentException("A trusted root requires upstream certificate validation.", nameof(TrustedRoot));
        }
        this.ValidateCertificate = ValidateCertificate;
        this.SniHost = SniHost;
        this.TrustedRoot = TrustedRoot;
    }

    public static RuntimeUpstreamTlsOptions Default { get; } = new(true, null);
    public bool ValidateCertificate { get; }
    public string? SniHost { get; }
    public RuntimeTrustedRootCertificate? TrustedRoot { get; }
}
