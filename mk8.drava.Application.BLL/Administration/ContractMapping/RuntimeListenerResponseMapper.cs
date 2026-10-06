using BusinessRuntimeHttp2LimitsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp2LimitsProjection;
using BusinessRuntimeListenerProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerProjection;
using BusinessRuntimeListenerProtocols = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerProtocols;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeListenerResponseMapper
{
    public static IReadOnlyList<RuntimeListenerResponse> FromListeners(IReadOnlyList<BusinessRuntimeListenerProjection> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        return ApiResponseList.Copy(listeners.Select(FromListener));
    }

    private static RuntimeListenerResponse FromListener(BusinessRuntimeListenerProjection listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new RuntimeListenerResponse(name: listener.Name, address: listener.Address, port: listener.Port, enabled: listener.Enabled, transport: RuntimeListenerTransportResponseMapper.FromTransport(listener.Transport), defaultCertificateId: listener.DefaultCertificateId, sniCertificates: RuntimeSniCertificateBindingResponseMapper.FromBindings(listener.SniCertificates), backlog: listener.Backlog, maxRequestHeadBytes: listener.MaxRequestHeadBytes, maxResponseHeadBytes: listener.MaxResponseHeadBytes, maxChunkLineBytes: listener.MaxChunkLineBytes, forwardingBufferBytes: listener.ForwardingBufferBytes, identity: RuntimeListenerIdentityResponseMapper.FromProjection(listener.Identity), protocols: RuntimeListenerProtocolsResponseMapper.FromProtocols(listener.Protocols), http3Enablement: RuntimeHttp3EnablementResponseMapper.FromEnablement(listener.Http3Enablement), http3AltSvc: RuntimeHttp3AltSvcResponseMapper.FromProjection(listener.Http3AltSvc), http2Limits: RuntimeHttp2LimitsResponseMapper.FromProjection(listener.Http2Limits), tcpTrafficEnabled: listener.TcpTrafficEnabled, http3ProtocolConfigured: listener.Http3ProtocolConfigured, quicIdentity: listener.QuicIdentity is null ? null : RuntimeQuicListenerIdentityResponseMapper.FromProjection(listener.QuicIdentity), http3: RuntimeHttp3ListenerReadinessResponseMapper.FromProjection(listener.Http3));
    }
}
