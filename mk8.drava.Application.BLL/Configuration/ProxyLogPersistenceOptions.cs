namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class ProxyLogPersistenceOptions
{
    public bool AccessLogEnabled { get; init; } = true;
    public bool AdminAuditEnabled { get; init; } = true;
    public long MaxFileBytes { get; init; } = 1_048_576;
    public int MaxFiles { get; init; } = 8;
}
