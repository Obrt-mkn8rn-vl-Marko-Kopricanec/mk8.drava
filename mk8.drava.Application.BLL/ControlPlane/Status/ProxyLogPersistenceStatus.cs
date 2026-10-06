using Mk8.Drava.Application.BLL.ControlPlane.Observability;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyLogPersistenceStatus
{
    private ProxyLogPersistenceStatus(bool accessLogEnabled, bool adminAuditEnabled, string? logDirectory, long maxFileBytes, int maxFiles, string state, string reason, DateTimeOffset? lastSuccessfulWriteAtUtc, ProxyLogPersistenceFailureStatus? lastWriteFailure)
    {
        AccessLogEnabled = accessLogEnabled;
        AdminAuditEnabled = adminAuditEnabled;
        LogDirectory = logDirectory;
        MaxFileBytes = maxFileBytes;
        MaxFiles = maxFiles;
        State = state;
        Reason = reason;
        LastSuccessfulWriteAtUtc = lastSuccessfulWriteAtUtc;
        LastWriteFailure = lastWriteFailure;
    }

    public bool AccessLogEnabled { get; }
    public bool AdminAuditEnabled { get; }
    public string? LogDirectory { get; }
    public long MaxFileBytes { get; }
    public int MaxFiles { get; }
    public string State { get; }
    public string Reason { get; }
    public DateTimeOffset? LastSuccessfulWriteAtUtc { get; }
    public ProxyLogPersistenceFailureStatus? LastWriteFailure { get; }
    public static ProxyLogPersistenceStatus Unknown { get; } = NoActiveConfiguration(logDirectory: null, ProxyLogPersistenceSettings.Unavailable);

    public static ProxyLogPersistenceStatus NoActiveConfiguration(string? logDirectory, ProxyLogPersistenceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Create(settings, logDirectory, ProxyStatusText.Unknown, ProxyStatusText.NoActiveConfig, lastSuccessfulWriteAtUtc: null, lastWriteFailure: null);
    }

    public static ProxyLogPersistenceStatus FromSettings(string? logDirectory, ProxyLogPersistenceSettings settings, DateTimeOffset? lastSuccessfulWriteAtUtc, ProxyLogPersistenceFailureStatus? lastWriteFailure)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var state = ProxyStatusText.Healthy;
        var reason = ProxyStatusText.Ready;
        if (!settings.AccessLogEnabled && !settings.AdminAuditEnabled)
        {
            state = ProxyStatusText.Disabled;
            reason = ProxyStatusText.Disabled;
        }
        else if (lastWriteFailure is not null && (lastSuccessfulWriteAtUtc is null || lastWriteFailure.TimestampUtc >= lastSuccessfulWriteAtUtc))
        {
            state = ProxyStatusText.Degraded;
            reason = ProxyStatusText.LastWriteFailed;
        }

        return Create(settings, logDirectory, state, reason, lastSuccessfulWriteAtUtc, lastWriteFailure);
    }

    private static ProxyLogPersistenceStatus Create(ProxyLogPersistenceSettings settings, string? logDirectory, string state, string reason, DateTimeOffset? lastSuccessfulWriteAtUtc, ProxyLogPersistenceFailureStatus? lastWriteFailure)
    {
        return new ProxyLogPersistenceStatus(settings.AccessLogEnabled, settings.AdminAuditEnabled, logDirectory, settings.MaxFileBytes, settings.MaxFiles, state, reason, lastSuccessfulWriteAtUtc, lastWriteFailure);
    }
}
