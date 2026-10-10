namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record RuntimeUpstreamTlsResponse(bool ValidateCertificate, string? SniHost)
{
    public RuntimeTrustedRootCertificateResponse? TrustedRoot { get; init; }
}
