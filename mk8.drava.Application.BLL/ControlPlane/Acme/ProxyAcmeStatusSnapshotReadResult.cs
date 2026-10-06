namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public abstract record ProxyAcmeStatusSnapshotReadResult
{
    private ProxyAcmeStatusSnapshotReadResult()
    {
    }

    public static ProxyAcmeStatusSnapshotReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyAcmeStatusSnapshotReadResult Available(ProxyAcmeStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AvailableResult(snapshot);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyAcmeStatusSnapshotReadResult
    {
        public AvailableResult(ProxyAcmeStatusSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Snapshot = snapshot;
        }

        public ProxyAcmeStatusSnapshot Snapshot { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyAcmeStatusSnapshotReadResult;
}
