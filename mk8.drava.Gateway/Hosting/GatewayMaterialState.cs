using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Presentation.Certificates;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Gateway.Hosting;

internal sealed class GatewayMaterialState : IDisposable
{
    private readonly Lock _gate = new();
    private readonly HashSet<Entry> _entries = [];
    private Entry? _current;
    private bool _disposed;

    public PresentationPlan? Read()
    {
        lock (_gate) return _current?.Material.Plan;
    }

    public bool Matches(PresentationPlan plan)
    {
        lock (_gate) return PresentationPlanDigest.Verify(plan) && _current?.Material.Plan.ContentSha256.Equals(plan.ContentSha256) == true;
    }

    public void Install(GatewayServingMaterial material)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var plan = material.Plan;
            if (_current is not null)
            {
                var current = _current.Material.Plan;
                if (plan.Generation < current.Generation || plan.Generation == current.Generation && !plan.ContentSha256.Equals(current.ContentSha256))
                    throw new InvalidDataException("Presentation plan regressed or changed without a new generation.");
            }
            if (_entries.Count >= 16) throw new InvalidDataException("Presentation material is waiting for older TLS connections to drain.");
            var replacement = new Entry(material);
            _entries.Add(replacement);
            var previous = _current;
            _current = replacement;
            if (previous is not null) { previous.Retired = true; Prune(previous); }
        }
    }

    public MaterialLease Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is null || _current.Material.Plan.ValidUntilUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return new MaterialLease(this, null);
            _current.References++;
            return new MaterialLease(this, _current);
        }
    }

    public bool ValidateClientCertificate(X509Certificate2 certificate)
    {
        lock (_gate) return !_disposed && _current is not null && _current.Material.Plan.ValidUntilUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            && _current.Material.ValidateClientCertificate(certificate);
    }

    public bool HasCurrent => Read() is { } plan && plan.ValidUntilUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static X509Certificate2? SelectCertificate(ConnectionContext? connection) => connection?.Features.Get<MaterialLease>()?.Certificate;

    private void Release(Entry entry)
    {
        lock (_gate) { entry.References--; Prune(entry); }
    }

    private void Prune(Entry entry)
    {
        if (entry.Retired && entry.References == 0) { _entries.Remove(entry); entry.Material.Dispose(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _current = null;
            foreach (var entry in _entries.ToArray()) { entry.Retired = true; Prune(entry); }
        }
    }

    internal sealed class Entry(GatewayServingMaterial material)
    {
        public GatewayServingMaterial Material { get; } = material;
        public int References { get; set; }
        public bool Retired { get; set; }
    }

    internal sealed class MaterialLease(GatewayMaterialState owner, Entry? entry) : IDisposable
    {
        private Entry? _entry = entry;
        public X509Certificate2? Certificate => _entry?.Material.ServingCertificate;
        public void Dispose() { var owned = Interlocked.Exchange(ref _entry, null); if (owned is not null) owner.Release(owned); }
    }
}
