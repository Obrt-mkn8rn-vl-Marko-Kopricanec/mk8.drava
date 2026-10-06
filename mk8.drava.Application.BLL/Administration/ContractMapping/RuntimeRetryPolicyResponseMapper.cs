using BusinessRuntimeCacheProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeCacheProjection;
using BusinessRuntimeRetryProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeRetryProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeRetryPolicyResponseMapper
{
    public static RuntimeRetryPolicyResponse FromProjection(BusinessRuntimeRetryProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeRetryPolicyResponse(enabled: projection.Enabled, maxAttempts: projection.MaxAttempts, perAttemptTimeout: projection.PerAttemptTimeout, retryOnConnectFailure: projection.RetryOnConnectFailure, retryOnUpstreamResponseHeadTimeout: projection.RetryOnUpstreamResponseHeadTimeout, retryOnStatusCodes: projection.RetryOnStatusCodes, retryMethods: projection.RetryMethods, retryBackoff: projection.RetryBackoff);
    }
}
