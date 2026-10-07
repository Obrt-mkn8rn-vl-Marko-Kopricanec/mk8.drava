using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Administration;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed class CloudflareDnsPublisher : IServiceDnsPublisher, IDisposable
{
    private const int MaximumCachedHosts = 10000;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _admission = new(4, 4);
    private readonly Dictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly CloudflareDnsApi _api;
    private readonly DnsPublicationSettings _settings;
    private readonly string _siteDomain;
    private readonly string _ownership;
    private readonly Dictionary<string, string> _addresses = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private bool _zoneChecked;

    public CloudflareDnsPublisher(DnsPublicationSettings settings, string siteDomain, string siteId, IReadOnlyList<string> addresses, TimeProvider clock)
        : this(settings, siteDomain, siteId, addresses, clock, null) { }

    internal CloudflareDnsPublisher(DnsPublicationSettings settings, string siteDomain, string siteId, IReadOnlyList<string> addresses, TimeProvider clock, HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(addresses); ArgumentNullException.ThrowIfNull(clock);
        settings.Validate(siteDomain); RegistryNames.RequireLabel(siteId);
        if (!string.Equals(settings.Provider, "cloudflare", StringComparison.Ordinal) || addresses.Count is < 1 or > 64)
            throw new InvalidDataException("DNS publisher requires Cloudflare settings and bounded Gateway addresses.");
        foreach (var value in addresses)
        {
            if (!IPAddress.TryParse(value, out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                address.IsIPv6Multicast || address.IsIPv4MappedToIPv6 || address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0 ||
                address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224)
                throw new InvalidDataException("DNS publication requires concrete unicast Gateway addresses.");
            _addresses.TryAdd(address.ToString(), address.AddressFamily == AddressFamily.InterNetwork ? "A" : "AAAA");
        }
        _settings = settings; _siteDomain = siteDomain; _ownership = "mk8.drava site=" + siteId; _clock = clock;
        _api = new CloudflareDnsApi(settings.ZoneId, PrivateBearerCredentialFile.Read(settings.CredentialPath), handler);
    }

    public async ValueTask<bool> EnsureAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsScopedHost(host)) return false;
        lock (_gate) if (TryReadAttempt(host, out var result)) return result;
        // No unbounded waiter/task queue: a later reconcile retries a saturated or already active host.
        if (!await _admission.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return false;
        var active = false;
        try
        {
            lock (_gate)
            {
                if (TryReadAttempt(host, out var result)) return result;
                if (_active.Contains(host)) return false;
                if (_attempts.Count >= MaximumCachedHosts)
                {
                    foreach (var key in _attempts.Where(pair => _clock.GetElapsedTime(pair.Value.Timestamp) >= TimeSpan.FromSeconds(_settings.RetrySeconds)).Select(static pair => pair.Key).ToArray()) _attempts.Remove(key);
                    if (_attempts.Count >= MaximumCachedHosts) return false;
                }
                active = _active.Add(host);
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
            bool confirmed;
            try { confirmed = await EnsureRecordsAsync(host, deadline.Token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException ||
                exception is OperationCanceledException && !cancellationToken.IsCancellationRequested) { confirmed = false; }
            lock (_gate) if (_attempts.Count < MaximumCachedHosts) _attempts[host] = new Attempt(_clock.GetTimestamp(), confirmed);
            return confirmed;
        }
        finally
        {
            if (active) lock (_gate) _active.Remove(host);
            _admission.Release();
        }
    }

    private async ValueTask<bool> EnsureRecordsAsync(string host, CancellationToken cancellationToken)
    {
        if (!Volatile.Read(ref _zoneChecked))
        {
            if (!string.Equals(await _api.ReadZoneNameAsync(cancellationToken).ConfigureAwait(false), _settings.ZoneName, StringComparison.OrdinalIgnoreCase)) return false;
            Volatile.Write(ref _zoneChecked, true);
        }
        for (var ancestor = host[(host.IndexOf('.', StringComparison.Ordinal) + 1)..]; !string.Equals(ancestor, _settings.ZoneName, StringComparison.Ordinal); ancestor = ancestor[(ancestor.IndexOf('.', StringComparison.Ordinal) + 1)..])
        {
            if (string.Equals(host, _settings.ZoneName, StringComparison.Ordinal)) break;
            using var delegation = await _api.ListAsync(ancestor, cancellationToken, nameServersOnly: true).ConfigureAwait(false);
            if (delegation.RootElement.GetProperty("result").GetArrayLength() != 0) return false;
        }
        using var response = await _api.ListAsync(host, cancellationToken).ConfigureAwait(false);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in response.RootElement.GetProperty("result").EnumerateArray())
        {
            if (!string.Equals(record.GetProperty("name").GetString(), host, StringComparison.OrdinalIgnoreCase) ||
                record.GetProperty("proxied").ValueKind != JsonValueKind.False ||
                !IPAddress.TryParse(record.GetProperty("content").GetString(), out var address) ||
                !_addresses.TryGetValue(address.ToString(), out var type) ||
                !string.Equals(record.GetProperty("type").GetString(), type, StringComparison.Ordinal) || !found.Add(address.ToString())) return false;
        }
        foreach (var address in _addresses)
            if (!found.Contains(address.Key) && !await _api.CreateAsync(host, address.Value, address.Key, _settings.TtlSeconds, _ownership, cancellationToken).ConfigureAwait(false)) return false;
        return true;
    }

    private bool IsScopedHost(string host)
    {
        if (host.Length > 253 || host.Count(static character => character == '.') > 15 || (!string.Equals(host, _siteDomain, StringComparison.Ordinal) && !host.EndsWith("." + _siteDomain, StringComparison.Ordinal))) return false;
        try { _ = new PublishedServiceAddress(host, "/"); return true; }
        catch (InvalidDataException) { return false; }
    }

    private bool TryReadAttempt(string host, out bool confirmed)
    {
        if (_attempts.TryGetValue(host, out var attempt))
        {
            var elapsed = _clock.GetElapsedTime(attempt.Timestamp);
            if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(_settings.RetrySeconds)) { confirmed = attempt.Confirmed; return true; }
            _attempts.Remove(host);
        }
        confirmed = false; return false;
    }

    public void Dispose() { _api.Dispose(); _admission.Dispose(); }
    private sealed record Attempt(long Timestamp, bool Confirmed);
}
