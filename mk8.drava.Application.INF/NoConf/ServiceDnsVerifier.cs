using System.Net;
using DnsClient;
using DnsClient.Protocol;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed class ServiceDnsVerifier : IServiceDnsVerifier
{
    private readonly LookupClient _client;
    private readonly HashSet<IPAddress> _approved;

    public ServiceDnsVerifier(IReadOnlyList<string> gatewayAddresses, string resolverAddress, int resolverPort)
    {
        ArgumentNullException.ThrowIfNull(gatewayAddresses);
        ArgumentNullException.ThrowIfNull(resolverAddress);
        _approved = new HashSet<IPAddress>();
        foreach (var address in gatewayAddresses) _approved.Add(IPAddress.Parse(address));
        if (_approved.Count is < 1 or > 64 || _approved.Contains(IPAddress.Any) || _approved.Contains(IPAddress.IPv6Any))
            throw new InvalidDataException("DNS publication requires approved concrete Gateway addresses.");
        var options = resolverAddress.Length == 0 ? new LookupClientOptions() : new LookupClientOptions(new IPEndPoint(IPAddress.Parse(resolverAddress), resolverPort));
        options.UseCache = false;
        options.Retries = 0;
        options.Timeout = TimeSpan.FromSeconds(2);
        options.ExtendedDnsBufferSize = 1232;
        options.UseTcpFallback = true;
        options.ThrowDnsErrors = false;
        _client = new LookupClient(options);
    }

    public async ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var ipv4 = await _client.QueryAsync(host, QueryType.A, QueryClass.IN, deadline.Token).ConfigureAwait(false);
            var ipv6 = await _client.QueryAsync(host, QueryType.AAAA, QueryClass.IN, deadline.Token).ConfigureAwait(false);
            if (ipv4.HasError || ipv6.HasError) return DnsPublicationProof.Missing;
            var count = 0;
            var ttl = 30;
            if (!Validate(host, ipv4.Answers, ref count, ref ttl) || !Validate(host, ipv6.Answers, ref count, ref ttl) || count == 0 || ttl <= 0) return DnsPublicationProof.Missing;
            return new DnsPublicationProof(true, TimeSpan.FromSeconds(ttl));
        }
        catch (DnsResponseException) { return DnsPublicationProof.Missing; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return DnsPublicationProof.Missing; }
    }

    private bool Validate(string host, IReadOnlyList<DnsResourceRecord> records, ref int count, ref int ttl)
    {
        if (records.Count > 64) return false;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host.TrimEnd('.') + "." };
        for (var pass = 0; pass < 8; pass++)
            foreach (var record in records)
                if (record is CNameRecord alias && allowed.Contains(alias.DomainName.Value))
                {
                    allowed.Add(alias.CanonicalName.Value);
                    ttl = Math.Min(ttl, alias.InitialTimeToLive);
                }
        foreach (var record in records)
        {
            var address = record switch { ARecord a => a.Address, AaaaRecord aaaa => aaaa.Address, _ => null };
            if (address is null) continue;
            if (!allowed.Contains(record.DomainName.Value) || !_approved.Contains(address)) return false;
            ttl = Math.Min(ttl, record.InitialTimeToLive);
            count++;
        }
        return true;
    }
}
