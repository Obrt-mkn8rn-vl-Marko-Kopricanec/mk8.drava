using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.INF.Proxy.Forwarding;
internal static class ProxyGeneratedFailureWriter
{
    public static ValueTask WriteAsync(Stream clientStream, ProxyFailureKind failureKind, RuntimeTimeouts timeouts, string requestId, ProxyMetrics metrics, CancellationToken cancellationToken)
    {
        var response = ProxyGeneratedFailurePolicy.BuildFailureResponse(failureKind);
        ProxyGeneratedFailureMetrics.Record(metrics, response);
        return ProxyErrorResponses.WriteGeneratedFailureAsync(clientStream, response, requestId, timeouts.DownstreamWriteTimeout, metrics, cancellationToken);
    }
}
