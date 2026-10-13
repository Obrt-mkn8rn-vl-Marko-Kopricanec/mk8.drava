namespace Mk8.Drava.Application.BLL.Configuration;

public sealed class TrustedRootCertificateOptions
{
    public string MaterialId { get; init; } = "";
    public string Sha256 { get; init; } = "";
}
