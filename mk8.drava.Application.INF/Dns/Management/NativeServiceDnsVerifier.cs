using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Dns.Management;

public sealed class NativeServiceDnsVerifier : IServiceDnsVerifier, IDisposable
{
    private readonly NativeDnsSession _session;
    private readonly NativeDnsServingVerifier _serving;
    private readonly NativeDnsManagementSettings _native;
    private readonly string _site;
    private readonly uint _ttl;
    private readonly TimeSpan _timeout;
    private readonly Dictionary<ushort, ReadOnlyMemory<byte>[]> _values;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NativeServiceDnsVerifier(DnsPublicationSettings settings, string siteDomain, string siteId,
        IReadOnlyList<string> addresses, string authority, int port, IDnsMutationJournal journal)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(addresses);
        settings.Validate(siteDomain);
        if (!string.Equals(settings.Provider, "mk8.dns", StringComparison.Ordinal) || settings.NativeManagement is null || addresses.Count is < 1 or > 64)
            throw new InvalidDataException("Native DNS requires its explicit private management profile and bounded Gateway addresses.");
        var parsed = new List<IPAddress>();
        foreach (var value in addresses)
        {
            if (!IPAddress.TryParse(value, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                address.IsIPv6Multicast || address.IsIPv4MappedToIPv6 || address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0 ||
                address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224)
                throw new InvalidDataException("Native publication requires concrete unicast Gateway addresses.");
            if (!parsed.Contains(address)) parsed.Add(address);
        }
        _values = new Dictionary<ushort, ReadOnlyMemory<byte>[]>();
        foreach (var type in new ushort[] { 1, 28 })
            _values.Add(type, parsed.Where(address => (address.AddressFamily == AddressFamily.InterNetwork ? 1 : 28) == type)
                .Select(static address => (ReadOnlyMemory<byte>)address.GetAddressBytes()).ToArray());
        _native = settings.NativeManagement; _site = siteDomain; _ttl = checked((uint)settings.TtlSeconds); _timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        _serving = new NativeDnsServingVerifier(authority, port, _native.Origin);
        _session = new NativeDnsSession(_native, siteDomain, siteId, acme: false, journal, parsed.Select(static address => address.ToString()).ToArray());
    }

    public async ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(host, _site, StringComparison.Ordinal) && !host.EndsWith("." + _site, StringComparison.Ordinal) ||
            !await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return DnsPublicationProof.Missing;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(_timeout);
        try
        {
            if (!await RecoverHostAsync(host, deadline.Token).ConfigureAwait(false)) return DnsPublicationProof.Missing;
            ManagementReply? snapshot = null;
            foreach (var pair in _values)
            {
                if (pair.Value.Length == 0) continue;
                if (!_native.Scopes.Any(scope => string.Equals(scope.Owner, host, StringComparison.Ordinal) && scope.Type == pair.Key)) return DnsPublicationProof.Missing;
                var reply = await _session.ReadAsync(host, pair.Key, deadline.Token).ConfigureAwait(false);
                if (snapshot is not null && (snapshot.Revision != reply.Revision || snapshot.Serial != reply.Serial || !string.Equals(snapshot.ContentHash, reply.ContentHash, StringComparison.Ordinal)))
                    return DnsPublicationProof.Missing;
                snapshot = reply;
                var found = new HashSet<string>(StringComparer.Ordinal);
                var approved = pair.Value.Select(static value => Convert.ToBase64String(value.Span)).ToHashSet(StringComparer.Ordinal);
                foreach (var record in reply.Records)
                {
                    var value = Convert.ToBase64String(record.Data.Span);
                    if (record.Ttl != _ttl || !approved.Contains(value) || !found.Add(value)) return DnsPublicationProof.Missing;
                }
                foreach (var value in pair.Value)
                    if (!found.Contains(Convert.ToBase64String(value.Span)))
                    {
                        var intent = _session.Intent(host, pair.Key, _ttl, value, reply.Revision, Guid.NewGuid());
                        _ = await _session.PrepareAndSendAsync(intent, deadline.Token).ConfigureAwait(false);
                        // At most one new mutation per reconciliation; never admit from its receipt alone.
                        return DnsPublicationProof.Missing;
                    }
            }
            if (snapshot is null) return DnsPublicationProof.Missing;
            foreach (var pair in _values)
                if (!await _serving.VerifyAsync(host, pair.Key, pair.Value, snapshot.Serial, deadline.Token).ConfigureAwait(false))
                    return DnsPublicationProof.Missing;
            // Bound reuse below every accepted TTL without claiming recursive/secondary convergence.
            return new DnsPublicationProof(true, TimeSpan.FromSeconds(1));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or FormatException or InvalidOperationException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        { cancellationToken.ThrowIfCancellationRequested(); return DnsPublicationProof.Missing; }
        finally { _gate.Release(); }
    }

    private async ValueTask<bool> RecoverHostAsync(string host, CancellationToken cancellationToken)
    {
        var state = await _session.ReadJournalAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in state.Entries)
        {
            if (!string.Equals(entry.Owner, host, StringComparison.Ordinal)) continue;
            if (string.Equals(entry.State, "rejected", StringComparison.Ordinal)) return false;
            if (entry.State is "prepared" or "accepted")
            {
                var current = await _session.RecoverAsync(entry, cancellationToken).ConfigureAwait(false);
                if (current is null || !string.Equals(current.State, "activated", StringComparison.Ordinal)) return false;
            }
        }
        return true;
    }

    public void Dispose() { _session.Dispose(); _gate.Dispose(); }
}
