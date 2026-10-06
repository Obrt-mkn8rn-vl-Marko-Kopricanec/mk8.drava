using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;

namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public interface IProxyAccessLogMetricsSink
{
    void RequestFailed(ProxyFailureKind failureKind);
    void RequestCompleted(string? site, string? route, string? action, int? statusCode);
    void AccessLogEmitted();
}
