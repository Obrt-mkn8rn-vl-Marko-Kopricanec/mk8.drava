namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeLogPersistenceOptions
{
    public RuntimeLogPersistenceOptions(bool AccessLogEnabled, bool AdminAuditEnabled, long MaxFileBytes, int MaxFiles)
    {
        RuntimeLogPersistenceFacts.Validate(MaxFileBytes, MaxFiles);
        this.AccessLogEnabled = AccessLogEnabled;
        this.AdminAuditEnabled = AdminAuditEnabled;
        this.MaxFileBytes = MaxFileBytes;
        this.MaxFiles = MaxFiles;
    }

    public bool AccessLogEnabled { get; }
    public bool AdminAuditEnabled { get; }
    public long MaxFileBytes { get; }
    public int MaxFiles { get; }
}
