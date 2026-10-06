namespace Mk8.Drava.Application.BLL.Registry;

public sealed record DestinationPublication(long RouteRevision, long GatewayGeneration, bool DnsVerified,
    bool CertificateVerified, DateTimeOffset ValidUntilUtc)
{
    public bool IsValid(DateTimeOffset nowUtc) => RouteRevision > 0 && GatewayGeneration > 0 && DnsVerified && CertificateVerified && nowUtc < ValidUntilUtc;
}
