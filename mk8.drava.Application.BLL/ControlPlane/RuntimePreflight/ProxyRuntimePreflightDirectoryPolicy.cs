using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public static class ProxyRuntimePreflightDirectoryPolicy
{
    public static IReadOnlyList<ProxyRuntimePreflightDirectoryRequirement> ExpectedDirectories()
    {
        return[new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Data, "data_directory", ".", true), new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Config, "config_directory", "config", true), new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Sites, "sites_directory", "config/sites", true), new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Logs, "logs_directory", "logs", false), new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.Certificates, "certificates_directory", "certs", false), new ProxyRuntimePreflightDirectoryRequirement(ProxyRuntimePreflightDirectoryKind.State, "state_directory", "state", false)];
    }
}
