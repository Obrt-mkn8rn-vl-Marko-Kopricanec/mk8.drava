namespace Mk8.Drava.Application.BLL.Registry;

public sealed record DnsPublicationProof(bool Verified, TimeSpan Validity)
{
    public static DnsPublicationProof Missing { get; } = new(false, TimeSpan.FromSeconds(1));
}
