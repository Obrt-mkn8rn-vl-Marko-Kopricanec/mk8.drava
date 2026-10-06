using BusinessProxyListenerStatus = Mk8.Drava.Application.BLL.ControlPlane.Listeners.ProxyListenerStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyListenerStatusResponseMapper
{
    public static IReadOnlyList<ProxyListenerStatusResponse> FromStatuses(IReadOnlyList<BusinessProxyListenerStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return ApiResponseList.Copy(statuses.Select(FromStatus));
    }

    private static ProxyListenerStatusResponse FromStatus(BusinessProxyListenerStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyListenerStatusResponse(status.Name, status.Identity, status.BindKey, status.Kind, status.Address, status.Port, status.Transport, status.TlsEnabled, status.Protocols, ProxyListenerHttp3StatusResponseMapper.FromStatus(status.Http3), status.Http2MaxConcurrentStreams, status.Http2MaxHeaderListBytes, status.Http2MaxFrameSize, ProxyListenerStateResponseMapper.FromState(status.State), status.ActiveConnections, status.StartedAtUtc, status.StoppedAtUtc, status.LastError);
    }
}
