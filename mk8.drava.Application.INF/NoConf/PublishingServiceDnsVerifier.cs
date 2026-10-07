using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed class PublishingServiceDnsVerifier : IServiceDnsVerifier
{
    private readonly IServiceDnsVerifier _verifier;
    private readonly IServiceDnsPublisher _publisher;

    public PublishingServiceDnsVerifier(IServiceDnsVerifier verifier, IServiceDnsPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(verifier); ArgumentNullException.ThrowIfNull(publisher);
        _verifier = verifier; _publisher = publisher;
    }

    public async ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken)
    {
        var observed = await _verifier.VerifyAsync(host, cancellationToken).ConfigureAwait(false);
        if (observed.Verified) return observed;
        if (!await _publisher.EnsureAsync(host, cancellationToken).ConfigureAwait(false)) return DnsPublicationProof.Missing;
        return await _verifier.VerifyAsync(host, cancellationToken).ConfigureAwait(false);
    }
}
