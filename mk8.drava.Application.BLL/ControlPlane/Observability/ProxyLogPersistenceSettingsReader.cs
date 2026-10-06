using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Observability;
public sealed class ProxyLogPersistenceSettingsReader : IProxyLogPersistenceSettingsReader
{
    private readonly IProxyLogPersistenceSettingsSource _settingsSource;
    public ProxyLogPersistenceSettingsReader(IProxyLogPersistenceSettingsSource settingsSource)
    {
        _settingsSource = settingsSource;
    }

    public ProxyLogPersistenceSettingsReadResult ReadLogPersistenceSettings()
    {
        var result = _settingsSource.ReadLogPersistenceSettings();
        if (result is ProxyLogPersistenceSettingsSourceResult.AvailableResult available)
        {
            return ProxyLogPersistenceSettingsReadResult.Active(available.Settings);
        }

        return ProxyLogPersistenceSettingsReadResult.DisabledDefaults();
    }
}
