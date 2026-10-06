namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationReloadEventSink
{
    void LoadFailed(string sourceDirectory, IReadOnlyList<string> errors);
    void Loaded(int version, string sourceDirectory);
}
