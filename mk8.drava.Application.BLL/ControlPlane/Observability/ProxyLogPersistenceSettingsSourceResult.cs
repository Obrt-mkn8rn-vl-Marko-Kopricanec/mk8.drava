namespace Mk8.Drava.Application.BLL.ControlPlane.Observability;
public abstract record ProxyLogPersistenceSettingsSourceResult
{
    private ProxyLogPersistenceSettingsSourceResult()
    {
    }

    public static ProxyLogPersistenceSettingsSourceResult MissingConfiguration { get; } = new MissingConfigurationResult();

    public static ProxyLogPersistenceSettingsSourceResult Available(ProxyLogPersistenceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new AvailableResult(settings);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record AvailableResult : ProxyLogPersistenceSettingsSourceResult
    {
        public AvailableResult(ProxyLogPersistenceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Settings = settings;
        }

        public ProxyLogPersistenceSettings Settings { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record MissingConfigurationResult : ProxyLogPersistenceSettingsSourceResult;
}
