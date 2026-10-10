using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.INF.Dns;

namespace Mk8.Drava.Application.INF.Acme;

internal sealed class AcmeDns01PropagationVerifier : IAcmeDns01PropagationVerifier
{
    private readonly IPEndPoint _resolver;
    public AcmeDns01PropagationVerifier(string resolverAddress, int resolverPort)
    {
        _resolver = DnsObservationEndpoint.Parse(resolverAddress, resolverPort);
    }

    public async ValueTask<bool> VerifyAsync(string host, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host); ArgumentException.ThrowIfNullOrWhiteSpace(value);
        cancellationToken.ThrowIfCancellationRequested();
        var transactionId = checked((ushort)RandomNumberGenerator.GetInt32(65536));
        var query = AcmeDns01WireResponse.CreateQuery(host, transactionId);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var response = await OwnedDnsQuery.QueryAsync(_resolver, query, transactionId, deadline.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return AcmeDns01WireResponse.HasProof(response, transactionId, host, value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidDataException)
        { cancellationToken.ThrowIfCancellationRequested(); return false; }
    }
}
