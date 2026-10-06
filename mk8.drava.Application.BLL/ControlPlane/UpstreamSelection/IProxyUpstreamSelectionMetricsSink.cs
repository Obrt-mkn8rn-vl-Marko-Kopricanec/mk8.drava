namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
public interface IProxyUpstreamSelectionMetricsSink
{
    void UpstreamSelected(ProxyUpstreamSelectionMetric selection);
    void NoHealthyUpstream();
    void NoAvailableUpstream();
}
