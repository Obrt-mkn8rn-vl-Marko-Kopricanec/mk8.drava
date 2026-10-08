using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DnsClient;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.Dns;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed class ServiceDnsVerifier : IServiceDnsVerifier
{
    private readonly IPEndPoint[] _resolvers;
    private int _nextResolver = -1;
    private readonly HashSet<IPAddress> _approved;

    public ServiceDnsVerifier(IReadOnlyList<string> gatewayAddresses, string resolverAddress, int resolverPort)
    {
        ArgumentNullException.ThrowIfNull(gatewayAddresses);
        ArgumentNullException.ThrowIfNull(resolverAddress);
        _approved = new HashSet<IPAddress>();
        foreach (var address in gatewayAddresses) _approved.Add(IPAddress.Parse(address));
        if (_approved.Count is < 1 or > 64 || _approved.Contains(IPAddress.Any) || _approved.Contains(IPAddress.IPv6Any))
            throw new InvalidDataException("DNS publication requires approved concrete Gateway addresses.");
        if (resolverPort is < 1 or > 65535 || resolverAddress.Length > 0 && !IPAddress.TryParse(resolverAddress, out _))
            throw new InvalidDataException("DNS publication requires an approved resolver literal and port.");
        var options = resolverAddress.Length == 0 ? new LookupClientOptions() : new LookupClientOptions(new IPEndPoint(IPAddress.Parse(resolverAddress), resolverPort));
        // Retain only the pinned library's operating-system resolver discovery, never its detached query wrapper.
        _resolvers = new LookupClient(options).NameServers.Select(static server => new IPEndPoint(IPAddress.Parse(server.Address), server.Port)).ToArray();
        if (_resolvers.Length is < 1 or > 8) throw new InvalidDataException("DNS publication requires a bounded resolver set.");
    }

    public async ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        cancellationToken.ThrowIfCancellationRequested();
        host = host.TrimEnd('.');
        var resolver = _resolvers[unchecked((uint)Interlocked.Increment(ref _nextResolver)) % (uint)_resolvers.Length];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var ipv4 = await ReadAsync(resolver, host, 1, deadline.Token).ConfigureAwait(false);
            var ipv6 = await ReadAsync(resolver, host, 28, deadline.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ipv4.Valid || !ipv6.Valid) return DnsPublicationProof.Missing;
            var count = 0;
            var ttl = 30;
            if (!Validate(host, ipv4.Answers, ref count, ref ttl) || !Validate(host, ipv6.Answers, ref count, ref ttl) || count == 0 || ttl <= 0) return DnsPublicationProof.Missing;
            return new DnsPublicationProof(true, TimeSpan.FromSeconds(ttl));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return DnsPublicationProof.Missing; }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidDataException)
        { cancellationToken.ThrowIfCancellationRequested(); return DnsPublicationProof.Missing; }
    }

    private static async ValueTask<DnsAddressResponse.Result> ReadAsync(IPEndPoint resolver, string host, ushort type, CancellationToken cancellationToken)
    {
        var transaction = checked((ushort)RandomNumberGenerator.GetInt32(65536)); var query = DnsWire.CreateQuery(host, transaction, type);
        var response = await OwnedDnsQuery.QueryAsync(resolver, query, transaction, cancellationToken).ConfigureAwait(false);
        return DnsAddressResponse.Read(response, transaction, host, type);
    }

    private bool Validate(string host, IReadOnlyList<DnsAddressResponse.Answer> records, ref int count, ref int ttl)
    {
        if (records.Count > 64) return false;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host };
        for (var pass = 0; pass < 8; pass++)
            foreach (var record in records)
                if (record.Alias is { } alias && allowed.Contains(record.Owner))
                {
                    allowed.Add(alias);
                    ttl = (int)Math.Min((uint)ttl, record.Ttl);
                }
        foreach (var record in records)
        {
            var address = record.Address;
            if (address is null) continue;
            if (!allowed.Contains(record.Owner) || !_approved.Contains(address)) return false;
            ttl = (int)Math.Min((uint)ttl, record.Ttl);
            count++;
        }
        return true;
    }
}
