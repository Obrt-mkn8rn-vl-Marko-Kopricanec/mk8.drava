namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyLogPersistenceStatusResponse
{
    public ProxyLogPersistenceStatusResponse(bool accessLogEnabled, bool adminAuditEnabled, string? logDirectory, long maxFileBytes, int maxFiles, string state, string reason, DateTimeOffset? lastSuccessfulWriteAtUtc, ProxyLogPersistenceFailureStatusResponse? lastWriteFailure)
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
    public ProxyLogPersistenceFailureStatusResponse? LastWriteFailure { get; }
}
