using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal static class ProxyGeneratedFailureMetrics
{
    public static void Record(ProxyMetrics metrics, ProxyFailureKind failureKind)
    {
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(failureKind);
        Record(metrics, response);
    }

    public static void Record(ProxyMetrics metrics, ProxyGeneratedFailureResponse response)
    {
        metrics.GeneratedFailureResponse(response.StatusCode);
    }
}
