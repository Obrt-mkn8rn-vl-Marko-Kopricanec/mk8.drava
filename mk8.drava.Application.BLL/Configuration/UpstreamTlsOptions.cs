namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class UpstreamTlsOptions
{
    public bool ValidateCertificate { get; init; } = true;
    public string? SniHost { get; init; }
    public TrustedRootCertificateOptions? TrustedRoot { get; init; }
}
