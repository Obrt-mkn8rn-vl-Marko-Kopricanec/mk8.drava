using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.NoConf;

public sealed record CompiledNoConfSnapshot
{
    public CompiledNoConfSnapshot(long desiredRevision, ProxyConfigurationSnapshot snapshot, IReadOnlyDictionary<string, CompiledNoConfService> services)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(desiredRevision);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(services);
        DesiredRevision = desiredRevision;
        Snapshot = snapshot;
        Services = RuntimeList.CopyDictionary(services, StringComparer.Ordinal);
    }
    public long DesiredRevision { get; }
    public ProxyConfigurationSnapshot Snapshot { get; }
    public IReadOnlyDictionary<string, CompiledNoConfService> Services { get; }
}
