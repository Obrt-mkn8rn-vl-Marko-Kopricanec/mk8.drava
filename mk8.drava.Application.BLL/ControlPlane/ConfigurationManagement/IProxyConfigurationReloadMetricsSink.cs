namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationReloadMetricsSink
{
    void ConfigReloadSucceeded();
    void ConfigReloadFailed();
}
