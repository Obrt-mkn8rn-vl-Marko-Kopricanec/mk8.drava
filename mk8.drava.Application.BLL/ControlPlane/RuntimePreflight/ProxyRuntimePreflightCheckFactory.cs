using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public static class ProxyRuntimePreflightCheckFactory
{
    public static ProxyRuntimePreflightCheck UnsafePath(ProxyRuntimePreflightDirectoryRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return new ProxyRuntimePreflightCheck(requirement.Name, requirement.RelativePath, Exists: false, Created: false, CanRead: false, CanWrite: false, requirement.Critical ? ProxyStatusText.Error : ProxyStatusText.Warning, "unsafe_path");
    }

    public static ProxyRuntimePreflightCheck FromProbeResult(ProxyRuntimePreflightDirectoryRequirement requirement, ProxyRuntimeDirectoryProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(requirement);
        var classification = ProxyRuntimePreflightProbePolicy.Classify(result, requirement.Critical);
        return new ProxyRuntimePreflightCheck(requirement.Name, requirement.RelativePath, result.Exists, result.Created, result.CanRead, result.CanWrite, classification.Severity, classification.Reason);
    }
}
