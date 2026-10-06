using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Observability;
public static class ProxyLogPersistenceSettingsMapper
{
    public static ProxyLogPersistenceSettings FromSource(RuntimeLogPersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProxyLogPersistenceSettings(options.AccessLogEnabled, options.AdminAuditEnabled, options.MaxFileBytes, options.MaxFiles);
    }
}
