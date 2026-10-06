using BusinessRuntimeHttp2LimitsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp2LimitsProjection;
using BusinessRuntimeListenerProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerProjection;
using BusinessRuntimeListenerProtocols = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerProtocols;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeHttp2LimitsResponseMapper
{
    public static RuntimeHttp2LimitsResponse FromProjection(BusinessRuntimeHttp2LimitsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeHttp2LimitsResponse(projection.MaxConcurrentStreams, projection.MaxHeaderListBytes, projection.MaxFrameSize);
    }
}
