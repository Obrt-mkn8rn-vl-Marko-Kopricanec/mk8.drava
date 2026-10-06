namespace Mk8.Drava.Application.BLL.ControlPlane.Observability;
public abstract record ProxyLogPersistenceSettingsReadResult
{
    private ProxyLogPersistenceSettingsReadResult(ProxyLogPersistenceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
    }

    public ProxyLogPersistenceSettings Settings { get; }

    public static ProxyLogPersistenceSettingsReadResult Active(ProxyLogPersistenceSettings settings)
    {
        return new ActiveResult(settings);
    }

    public static ProxyLogPersistenceSettingsReadResult DisabledDefaults()
    {
        return new DisabledDefaultsResult(ProxyLogPersistenceSettings.DisabledOperationalDefaults);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ActiveResult : ProxyLogPersistenceSettingsReadResult
    {
        public ActiveResult(ProxyLogPersistenceSettings settings) : base(settings)
        {
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record DisabledDefaultsResult : ProxyLogPersistenceSettingsReadResult
    {
        public DisabledDefaultsResult(ProxyLogPersistenceSettings settings) : base(settings)
        {
        }
    }
}
