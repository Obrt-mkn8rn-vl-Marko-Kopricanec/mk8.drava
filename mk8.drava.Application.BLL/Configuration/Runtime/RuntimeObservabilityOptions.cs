namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeObservabilityOptions
{
    public RuntimeObservabilityOptions(bool AccessLogEnabled, int RecentDiagnosticsCapacity, RuntimeLogPersistenceOptions LogPersistence)
    {
        RuntimeObservabilityFacts.ValidateRecentDiagnosticsCapacity(RecentDiagnosticsCapacity);
        ArgumentNullException.ThrowIfNull(LogPersistence);
        this.AccessLogEnabled = AccessLogEnabled;
        this.RecentDiagnosticsCapacity = RecentDiagnosticsCapacity;
        this.LogPersistence = LogPersistence;
    }

    public bool AccessLogEnabled { get; }
    public int RecentDiagnosticsCapacity { get; }
    public RuntimeLogPersistenceOptions LogPersistence { get; }
}
