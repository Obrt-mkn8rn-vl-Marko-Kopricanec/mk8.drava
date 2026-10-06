namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeObservabilityProjection
{
    public RuntimeObservabilityProjection(bool AccessLogEnabled, int RecentDiagnosticsCapacity, RuntimeLogPersistenceProjection LogPersistence)
    {
        RuntimeObservabilityFacts.ValidateRecentDiagnosticsCapacity(RecentDiagnosticsCapacity);
        ArgumentNullException.ThrowIfNull(LogPersistence);
        this.AccessLogEnabled = AccessLogEnabled;
        this.RecentDiagnosticsCapacity = RecentDiagnosticsCapacity;
        this.LogPersistence = LogPersistence;
    }

    public bool AccessLogEnabled { get; }
    public int RecentDiagnosticsCapacity { get; }
    public RuntimeLogPersistenceProjection LogPersistence { get; }
}
