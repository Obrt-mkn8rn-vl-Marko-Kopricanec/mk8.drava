using BusinessRuntimeCircuitBreakerProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCircuitBreakerProjection;
using BusinessRuntimeUpstreamProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeUpstreamProjection;
using BusinessRuntimeUpstreamTlsProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeUpstreamTlsProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeCircuitBreakerResponseMapper
{
    public static RuntimeCircuitBreakerResponse FromProjection(BusinessRuntimeCircuitBreakerProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeCircuitBreakerResponse(enabled: projection.Enabled, failureThreshold: projection.FailureThreshold, samplingWindow: projection.SamplingWindow, openDuration: projection.OpenDuration, halfOpenMaxAttempts: projection.HalfOpenMaxAttempts, failureStatusCodes: projection.FailureStatusCodes);
    }
}
