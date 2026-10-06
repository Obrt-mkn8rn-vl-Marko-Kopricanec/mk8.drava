using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using System.Net;

namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public sealed class ProxyAdminAuthenticationService
{
    public const int UnauthorizedStatusCode = 401;
    public const int ForbiddenStatusCode = 403;
    public const int InternalServerErrorStatusCode = 500;
    public const string FailureResponseBody = "Admin authentication failed.";
    private readonly IProxyAdminSecurityOptionsReader _securityReader;
    private readonly IProxyAdminAuditRecorder _auditRecorder;
    private readonly IProxyAdminAuthenticationMetricsSink _metrics;
    private readonly IProxyAdminAuthenticationEventSink _events;
    private readonly TimeProvider _timeProvider;
    public ProxyAdminAuthenticationService(IProxyAdminSecurityOptionsReader securityReader, IProxyAdminAuditRecorder auditRecorder, IProxyAdminAuthenticationMetricsSink metrics, IProxyAdminAuthenticationEventSink events, TimeProvider timeProvider)
    {
        _securityReader = securityReader;
        _auditRecorder = auditRecorder;
        _metrics = metrics;
        _events = events;
        _timeProvider = timeProvider;
    }

    public ProxyAdminAuthenticationOutcome Authenticate(ProxyAdminRequestAuthenticationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var securityResult = _securityReader.Read();
        if (!securityResult.HasActiveConfiguration)
        {
            _events.ActiveConfigurationMissing();
        }

        var decision = ProxyAdminAuthenticationPolicy.Authenticate(new ProxyAdminAuthenticationInput(securityResult.RequireAuthentication, securityResult.Token, input.PresentedCredentials));
        if (decision.AuthenticationRequired && !decision.Allowed)
        {
            _metrics.AdminAuthFailed();
            var deniedStatusCode = decision.ShouldChallenge ? UnauthorizedStatusCode : ForbiddenStatusCode;
            RecordAudit(input, decision.Result, deniedStatusCode, succeeded: false, securityResult.RecentAuditCapacity);
            return new ProxyAdminAuthenticationOutcome(false, decision.Result, securityResult.RecentAuditCapacity, decision.ShouldChallenge, deniedStatusCode);
        }

        if (decision.AuthenticationRequired)
        {
            _metrics.AdminAuthSucceeded();
        }

        return new ProxyAdminAuthenticationOutcome(true, decision.Result, securityResult.RecentAuditCapacity, ShouldChallenge: false, DeniedStatusCode: null);
    }

    public void RecordCompleted(ProxyAdminRequestAuthenticationInput input, ProxyAdminAuthenticationOutcome outcome, int statusCode)
    {
        RecordOperationCompleted(input, outcome, statusCode, succeeded: statusCode < InternalServerErrorStatusCode);
    }

    public void RecordOperationCompleted(ProxyAdminRequestAuthenticationInput input, ProxyAdminAuthenticationOutcome outcome, int statusCode, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(input);
        RecordAudit(input, outcome.AuthResult, statusCode, succeeded: outcome.Allowed && succeeded, outcome.RecentAuditCapacity);
    }

    public void RecordFailed(ProxyAdminRequestAuthenticationInput input, ProxyAdminAuthenticationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(input);
        RecordAudit(input, outcome.AuthResult, InternalServerErrorStatusCode, succeeded: false, outcome.RecentAuditCapacity);
    }

    private void RecordAudit(ProxyAdminRequestAuthenticationInput input, string authResult, int statusCode, bool succeeded, int capacity)
    {
        _auditRecorder.Add(new ProxyAdminAuditEvent(_timeProvider.GetUtcNow(), input.Method, string.IsNullOrEmpty(input.Path) ? "/" : input.Path, input.RemoteClientAddress, authResult, statusCode, succeeded), capacity);
    }
}
