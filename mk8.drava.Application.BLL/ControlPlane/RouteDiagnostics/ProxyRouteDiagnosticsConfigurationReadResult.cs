namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public abstract record ProxyRouteDiagnosticsConfigurationReadResult
{
    private ProxyRouteDiagnosticsConfigurationReadResult()
    {
    }

    public static ProxyRouteDiagnosticsConfigurationReadResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyRouteDiagnosticsConfigurationReadResult Available(IProxyRouteDiagnosticsConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AvailableResult(snapshot);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyRouteDiagnosticsConfigurationReadResult
    {
        public AvailableResult(IProxyRouteDiagnosticsConfigurationSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Snapshot = snapshot;
        }

        public IProxyRouteDiagnosticsConfigurationSnapshot Snapshot { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyRouteDiagnosticsConfigurationReadResult;
}
