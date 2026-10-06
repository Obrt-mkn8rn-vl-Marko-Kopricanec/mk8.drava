using Mk8.Drava.Application.BLL.Administration.ContractMapping;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Administration;

public sealed partial class ProxyAdministrationDispatcher
{
    private ControlReply QueryConfiguration(ControlRequest request)
    {
        var query = request.JsonPayload.Length == 0 ? new ConfigurationQueryRequest() : ControlJson.Read<ConfigurationQueryRequest>(request.JsonPayload);
        var result = query.Effective ? reads.ReadEffective() : reads.ReadActive();
        return result is ProxyConfigurationReadResult<ProxyConfigurationProjection>.AvailableResult available
            ? ControlJson.Reply(ProxyConfigurationResponseMapper.FromProjection(available.Configuration))
            : new ControlReply { StatusCode = 404 };
    }

    private ControlReply Normalize(ControlRequest request)
    {
        var submission = ControlJson.Read<ProxyConfigurationNormalizeSubmissionRequest>(request.JsonPayload);
        var response = ProxyConfigurationNormalizeResponseMapper.FromResult(configuration.Normalize(submission.ToNormalizeRequest()));
        return ControlJson.Reply(response, response.Succeeded);
    }

    private async ValueTask<ControlReply> ValidateAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        RequireEmptyPayload(request);
        var result = await configuration.ValidateAsync(cancellationToken).ConfigureAwait(false);
        var response = ProxyConfigurationValidationResponseMapper.FromResult(result);
        return ControlJson.Reply(response, response.Succeeded);
    }

    private async ValueTask<ControlReply> ReloadAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        RequireEmptyPayload(request);
        var result = await reloads.ReloadAsync(cancellationToken).ConfigureAwait(false);
        var response = ProxyConfigurationReloadResponseMapper.FromResult(result);
        return ControlJson.Reply(response, response.Succeeded);
    }

    private ControlReply Lint(ControlRequest request)
    {
        var result = request.JsonPayload.Length == 0 ? lint.LintActive()
            : lint.LintSubmitted(ControlJson.Read<ProxyConfigLintSubmissionRequest>(request.JsonPayload).ToConfigLintRequest());
        var response = ConfigLintResponseMapper.FromResult(result);
        return ControlJson.Reply(response, response.Succeeded);
    }

    private ControlReply Match(ControlRequest request)
    {
        var submission = ControlJson.Read<ProxyRouteMatchDryRunRequest>(request.JsonPayload);
        var response = RouteMatchDryRunResponseMapper.FromResult(routes.Match(submission.ToRouteMatchDryRunRequest()));
        return ControlJson.Reply(response, response.Succeeded);
    }
}
