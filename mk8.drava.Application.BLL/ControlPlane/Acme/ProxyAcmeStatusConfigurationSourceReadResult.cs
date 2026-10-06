namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public abstract record ProxyAcmeStatusConfigurationSourceReadResult
{
    private ProxyAcmeStatusConfigurationSourceReadResult()
    {
    }

    public static ProxyAcmeStatusConfigurationSourceReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyAcmeStatusConfigurationSourceReadResult Available(ProxyAcmeStatusConfigurationSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AvailableResult(snapshot);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyAcmeStatusConfigurationSourceReadResult
    {
        public AvailableResult(ProxyAcmeStatusConfigurationSourceSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Snapshot = snapshot;
        }

        public ProxyAcmeStatusConfigurationSourceSnapshot Snapshot { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyAcmeStatusConfigurationSourceReadResult;
}
