namespace Mk8.Drava.Application.BLL.ControlPlane.Observability;
public sealed record ProxyLogPersistenceSettings(bool AccessLogEnabled, bool AdminAuditEnabled, long MaxFileBytes, int MaxFiles)
{
    public static ProxyLogPersistenceSettings DisabledOperationalDefaults { get; } = new(AccessLogEnabled: false, AdminAuditEnabled: false, MaxFileBytes: 1_048_576, MaxFiles: 8);
    public static ProxyLogPersistenceSettings Unavailable { get; } = new(AccessLogEnabled: false, AdminAuditEnabled: false, MaxFileBytes: 0, MaxFiles: 0);
}
