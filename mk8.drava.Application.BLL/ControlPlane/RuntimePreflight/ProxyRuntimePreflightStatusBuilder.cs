using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
public static class ProxyRuntimePreflightStatusBuilder
{
    public static ProxyRuntimePreflightStatus Build(DateTimeOffset generatedAtUtc, IEnumerable<ProxyRuntimePreflightCheck> checks, int maxReasons)
    {
        return ProxyRuntimePreflightStatus.Completed(generatedAtUtc, checks, maxReasons);
    }
}
