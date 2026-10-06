using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public interface IProxyConfigurationStore : IProxyActiveConfigurationSnapshotReader, IProxyActiveConfigurationSnapshotWriter
{
    bool HasActiveSnapshot { get; }
}
