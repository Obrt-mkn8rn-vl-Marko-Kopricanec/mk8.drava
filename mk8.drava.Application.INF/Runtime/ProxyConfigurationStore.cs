using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.INF.Runtime;
public sealed class ProxyConfigurationStore : IProxyConfigurationStore, IProxyActiveConfigurationVersionReader, IProxyStatusConfigurationSource
{
    private ProxyConfigurationSnapshot? _snapshot;
    public int? ActiveConfigVersion => Volatile.Read(ref _snapshot)?.Version;
    public bool HasActiveSnapshot => Volatile.Read(ref _snapshot)is not null;
    public ProxyConfigurationSnapshot Snapshot => Volatile.Read(ref _snapshot) ?? throw new InvalidOperationException("No active proxy configuration has been loaded.");

    public ProxyConfigurationSnapshotReadResult ReadSnapshot()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot is null ? ProxyConfigurationSnapshotReadResult.MissingSnapshot : ProxyConfigurationSnapshotReadResult.Available(snapshot);
    }

    public ProxyStatusConfigurationReadResult ReadConfiguration()
    {
        var snapshotResult = ReadSnapshot();
        if (snapshotResult is not ProxyConfigurationSnapshotReadResult.AvailableResult available)
        {
            return ProxyStatusConfigurationReadResult.MissingConfiguration;
        }

        return ProxyStatusConfigurationReadResult.Available(ProxyStatusConfigurationSourceMapper.FromSources(available.Snapshot.Version, available.Snapshot.LoadedAtUtc, available.Snapshot.Listeners, available.Snapshot.Routes, available.Snapshot.Certificates.Values, available.Snapshot.Acme, available.Snapshot.Limits, ProxyUpstreamHealthSourceMapper.FromRoutes(available.Snapshot.Routes)));
    }

    public ProxyConfigurationSnapshot Replace(ProxyConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _snapshot, snapshot);
        return snapshot;
    }
}
