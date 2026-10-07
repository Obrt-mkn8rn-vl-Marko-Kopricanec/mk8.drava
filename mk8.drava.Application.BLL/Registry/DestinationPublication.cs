namespace Mk8.Drava.Application.BLL.Registry;

public sealed record DestinationPublication(long RouteRevision, long GatewayGeneration, bool DnsVerified,
    bool CertificateVerified, DateTimeOffset ValidUntilUtc)
{
    public PublishedServiceAddress? Address { get; init; }
    public bool IsValid(DateTimeOffset nowUtc) => Address is not null && RouteRevision > 0 && GatewayGeneration > 0 && DnsVerified && CertificateVerified && nowUtc < ValidUntilUtc;
}
