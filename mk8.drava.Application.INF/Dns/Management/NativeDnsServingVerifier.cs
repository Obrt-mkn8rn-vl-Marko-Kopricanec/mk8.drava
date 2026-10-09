using System.Net;
using System.Security.Cryptography;
using Mk8.Drava.Application.INF.Dns;

namespace Mk8.Drava.Application.INF.Dns.Management;

internal sealed class NativeDnsServingVerifier
{
    private readonly IPEndPoint _endpoint;
    private readonly string _origin;

    public NativeDnsServingVerifier(string resolver, int port, string origin)
    {
        if (!IPAddress.TryParse(resolver, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || port is < 1 or > 65535)
            throw new InvalidDataException("Native DNS serving observation requires one explicit authority address and port.");
        _endpoint = new IPEndPoint(address, port); _origin = origin;
    }

    public async ValueTask<bool> VerifyAsync(string owner, ushort type, IReadOnlyList<ReadOnlyMemory<byte>> values, uint requiredSerial, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var first = await ReadSerialAsync(deadline.Token).ConfigureAwait(false);
        if (first is null || !NativeDnsServingResponse.IsAtLeast(first.Value, requiredSerial)) return false;
        var transaction = checked((ushort)RandomNumberGenerator.GetInt32(65536));
        var query = DnsWire.CreateQuery(owner, transaction, type); query[2] = 0;
        var response = await OwnedDnsQuery.QueryAsync(_endpoint, query, transaction, deadline.Token).ConfigureAwait(false);
        if (!NativeDnsServingResponse.HasValues(response, transaction, owner, type, values)) return false;
        var last = await ReadSerialAsync(deadline.Token).ConfigureAwait(false);
        return last == first;
    }

    public async ValueTask<bool> VerifyAbsentAsync(string owner, ushort type, ReadOnlyMemory<byte> value, uint requiredSerial, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var first = await ReadSerialAsync(deadline.Token).ConfigureAwait(false);
        if (first is null || !NativeDnsServingResponse.IsAtLeast(first.Value, requiredSerial)) return false;
        var transaction = checked((ushort)RandomNumberGenerator.GetInt32(65536));
        var query = DnsWire.CreateQuery(owner, transaction, type); query[2] = 0;
        var response = await OwnedDnsQuery.QueryAsync(_endpoint, query, transaction, deadline.Token).ConfigureAwait(false);
        if (!NativeDnsServingResponse.LacksValue(response, transaction, owner, type, value, _origin, requiredSerial)) return false;
        var last = await ReadSerialAsync(deadline.Token).ConfigureAwait(false);
        return last == first;
    }

    private async ValueTask<uint?> ReadSerialAsync(CancellationToken cancellationToken)
    {
        var transaction = checked((ushort)RandomNumberGenerator.GetInt32(65536));
        var query = DnsWire.CreateQuery(_origin, transaction, 6); query[2] = 0;
        var response = await OwnedDnsQuery.QueryAsync(_endpoint, query, transaction, cancellationToken).ConfigureAwait(false);
        return NativeDnsServingResponse.TryReadSerial(response, transaction, _origin, out var serial) ? serial : null;
    }
}
