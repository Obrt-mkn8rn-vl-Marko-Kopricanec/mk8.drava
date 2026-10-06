namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyReadinessEvaluationInput
{
    public ProxyReadinessEvaluationInput(bool HasActiveConfiguration, int? ConfigGeneration, bool IsShuttingDown, bool LastListenerReloadFailed, string LogPersistenceState, ProxyRuntimePreflightStatus RuntimePreflight, ProxySubsystemSummaries Subsystems, DateTimeOffset EvaluatedAtUtc)
    {
        ProxyStatusFacts.RequireOptionalNonNegative(ConfigGeneration, nameof(ConfigGeneration));
        this.HasActiveConfiguration = HasActiveConfiguration;
        this.ConfigGeneration = ConfigGeneration;
        this.IsShuttingDown = IsShuttingDown;
        this.LastListenerReloadFailed = LastListenerReloadFailed;
        this.LogPersistenceState = LogPersistenceState;
        this.RuntimePreflight = RuntimePreflight;
        this.Subsystems = Subsystems;
        this.EvaluatedAtUtc = EvaluatedAtUtc;
    }

    public bool HasActiveConfiguration { get; }
    public int? ConfigGeneration { get; }
    public bool IsShuttingDown { get; }
    public bool LastListenerReloadFailed { get; }
    public string LogPersistenceState { get; }
    public ProxyRuntimePreflightStatus RuntimePreflight { get; }
    public ProxySubsystemSummaries Subsystems { get; }
    public DateTimeOffset EvaluatedAtUtc { get; }
}
