using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public static class ProxyRetryRuntimeMapper
{
    public static ProxyRetryAdmissionInput ToAdmissionInput(RuntimeRoute route, Http1RequestHead requestHead)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(requestHead);
        return new ProxyRetryAdmissionInput(route.Retry.Enabled, route.Retry.MaxAttempts, route.Retry.RetryMethods, requestHead.Method, requestHead.Framing.Kind != Http1BodyKind.None);
    }

    public static ProxyRetryOutcomeInput ToOutcomeInput(RuntimeRetryPolicy retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        return new ProxyRetryOutcomeInput(retry.Enabled, retry.RetryOnConnectFailure, retry.RetryOnUpstreamResponseHeadTimeout, retry.RetryOnStatusCodes);
    }
}
