namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public interface IProxyRateLimitMetricsSink
{
    void RequestRateLimited();
    void UpgradeRateLimited();
}
