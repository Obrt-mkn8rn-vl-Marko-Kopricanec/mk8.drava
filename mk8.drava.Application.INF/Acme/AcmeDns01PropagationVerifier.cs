using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DnsClient;
using Mk8.Drava.Application.INF.Dns;

namespace Mk8.Drava.Application.INF.Acme;

internal sealed class AcmeDns01PropagationVerifier : IAcmeDns01PropagationVerifier
{
    private readonly IPEndPoint[] _resolvers;
    private int _nextResolver = -1;
    public AcmeDns01PropagationVerifier(string resolverAddress, int resolverPort)
    {
        ArgumentNullException.ThrowIfNull(resolverAddress);
        if (resolverPort is < 1 or > 65535 || resolverAddress.Length > 0 && !IPAddress.TryParse(resolverAddress, out _))
            throw new InvalidDataException("ACME propagation requires an approved DNS resolver literal and port.");
        var options = resolverAddress.Length == 0 ? new LookupClientOptions() : new LookupClientOptions(new IPEndPoint(IPAddress.Parse(resolverAddress), resolverPort));
        // Reuse DnsClient's operating-system resolver discovery, never its cancellation-racing query wrapper.
        _resolvers = new LookupClient(options).NameServers.Select(static server => new IPEndPoint(IPAddress.Parse(server.Address), server.Port)).ToArray();
        if (_resolvers.Length is < 1 or > 8) throw new InvalidDataException("DNS01 requires a bounded owner-approved resolver set.");
    }

    public async ValueTask<bool> VerifyAsync(string host, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host); ArgumentException.ThrowIfNullOrWhiteSpace(value);
        cancellationToken.ThrowIfCancellationRequested();
        var transactionId = checked((ushort)RandomNumberGenerator.GetInt32(65536));
        var query = AcmeDns01WireResponse.CreateQuery(host, transactionId);
        var resolver = _resolvers[unchecked((uint)Interlocked.Increment(ref _nextResolver)) % (uint)_resolvers.Length];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var response = await OwnedDnsQuery.QueryAsync(resolver, query, transactionId, deadline.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return AcmeDns01WireResponse.HasProof(response, transactionId, host, value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidDataException)
        { cancellationToken.ThrowIfCancellationRequested(); return false; }
    }
}
