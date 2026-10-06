using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public static class ProxyRetryPolicy
{
    public static ProxyRetryPlan CreatePlan(ProxyRetryAdmissionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var admission = EvaluateAdmission(input);
        var isAllowed = admission == ProxyRetryAdmissionDecision.Allowed;
        return new ProxyRetryPlan(admission, isAllowed, isAllowed ? Math.Max(1, input.MaxAttempts) : 1);
    }

    public static ProxyRetryAdmissionDecision EvaluateAdmission(ProxyRetryAdmissionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.Enabled)
        {
            return ProxyRetryAdmissionDecision.NotAllowed;
        }

        if (!input.RetryMethods.Any(method => string.Equals(method, input.RequestMethod, StringComparison.OrdinalIgnoreCase)))
        {
            return ProxyRetryAdmissionDecision.Skipped("method");
        }

        if (input.HasRequestBody)
        {
            return ProxyRetryAdmissionDecision.Skipped("request_body");
        }

        return ProxyRetryAdmissionDecision.Allowed;
    }

    public static ProxyRetryAttemptDecision EvaluateAttempt(ProxyRetryOutcomeInput input, ForwardingResult result, int attempt, int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!IsRetryableFailure(input, result))
        {
            return ProxyRetryAttemptDecision.Stop;
        }

        if (result.ResponseStarted)
        {
            return ProxyRetryAttemptDecision.Skipped("response_started");
        }

        return attempt < maxAttempts ? ProxyRetryAttemptDecision.Retry : ProxyRetryAttemptDecision.Stop;
    }

    public static bool ShouldSuppressRetryableStatusResponse(ProxyRetryOutcomeInput input, int statusCode, bool suppressRetryableStatusResponse)
    {
        ArgumentNullException.ThrowIfNull(input);
        return suppressRetryableStatusResponse && input.Enabled && input.RetryOnStatusCodes.Any(code => code == statusCode);
    }

    public static bool ShouldSuppressAttemptFailureResponse(bool retryAllowed, int attempt, int maxAttempts)
    {
        return retryAllowed && attempt < maxAttempts;
    }

    public static bool DidExhaustAttempts(ProxyRetryOutcomeInput input, ForwardingResult result, int attempt, int maxAttempts)
    {
        return attempt == maxAttempts && IsRetryableFailure(input, result);
    }

    public static bool DidExhaustAttemptsBeforeUpstreamSelection(int attempt)
    {
        return attempt > 1;
    }

    public static ForwardingResult RequireCompletedAttemptResult(ForwardingResult? result)
    {
        return result ?? throw new InvalidOperationException("Retry attempt loop completed without running an attempt.");
    }

    public static bool IsRetryableFailure(ProxyRetryOutcomeInput input, ForwardingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(input);
        if (result.ResponseStatusCode.HasValue && input.RetryOnStatusCodes.Any(code => code == result.ResponseStatusCode.Value))
        {
            return true;
        }

        if (result is ForwardingResult.FailureResult)
        {
            return result.FailureKind switch
            {
                ProxyFailureKind.UpstreamConnectFailed => input.RetryOnConnectFailure,
                ProxyFailureKind.UpstreamConnectTimeout => input.RetryOnConnectFailure,
                ProxyFailureKind.UpstreamResponseHeadTimeout => input.RetryOnUpstreamResponseHeadTimeout,
                _ => false
            };
        }

        return false;
    }
}
