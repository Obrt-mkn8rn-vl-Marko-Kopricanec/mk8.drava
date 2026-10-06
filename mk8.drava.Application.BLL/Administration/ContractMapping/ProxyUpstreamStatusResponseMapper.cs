using BusinessProxyUpstreamStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyUpstreamStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyUpstreamStatusResponseMapper
{
    public static IReadOnlyList<ProxyUpstreamStatusResponse> FromStatuses(IEnumerable<BusinessProxyUpstreamStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return ApiResponseList.Copy(statuses.Select(FromStatus));
    }

    public static ProxyUpstreamStatusResponse FromStatus(BusinessProxyUpstreamStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyUpstreamStatusResponse(status.RouteName, status.UpstreamName, status.Endpoint, status.Scheme, status.TlsCertificateValidationEnabled, status.SniHost, status.HealthCheckEnabled, UpstreamHealthStateResponseMapper.FromState(status.HealthState), status.LastHealthCheckResult, status.LastHealthCheckAtUtc, status.ConsecutiveSuccesses, status.ConsecutiveFailures, status.SelectedRequests, status.RequestFailures, status.Protocol, status.Weight, CircuitBreakerStatusResponseMapper.FromStatus(status.CircuitBreaker));
    }
}
