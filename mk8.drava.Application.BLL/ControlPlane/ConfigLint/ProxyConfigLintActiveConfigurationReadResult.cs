namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
public abstract record ProxyConfigLintActiveConfigurationReadResult
{
    private ProxyConfigLintActiveConfigurationReadResult()
    {
    }

    public static ProxyConfigLintActiveConfigurationReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyConfigLintActiveConfigurationReadResult Available(ProxyConfigLintConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AvailableResult(snapshot);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyConfigLintActiveConfigurationReadResult
    {
        public AvailableResult(ProxyConfigLintConfigurationSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Snapshot = snapshot;
        }

        public ProxyConfigLintConfigurationSnapshot Snapshot { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyConfigLintActiveConfigurationReadResult;
}
