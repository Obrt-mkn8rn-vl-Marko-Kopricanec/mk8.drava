namespace Mk8.Drava.Application.BLL.Registry;

public interface IServiceDnsVerifier
{
    ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken);
}
