using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public abstract record ProxyConfigurationSnapshotReadResult
{
    private ProxyConfigurationSnapshotReadResult()
    {
    }

    public static ProxyConfigurationSnapshotReadResult MissingSnapshot { get; } = new MissingSnapshotResult();

    public static ProxyConfigurationSnapshotReadResult Available(ProxyConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AvailableResult(snapshot);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyConfigurationSnapshotReadResult
    {
        public AvailableResult(ProxyConfigurationSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Snapshot = snapshot;
        }

        public ProxyConfigurationSnapshot Snapshot { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingSnapshotResult : ProxyConfigurationSnapshotReadResult;
}
