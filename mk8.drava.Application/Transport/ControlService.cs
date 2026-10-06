using Grpc.Core;
using System.Net;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.INF.Administration;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.Transport;

internal sealed class ControlService(ServingPlanState? plans, ProxyAdminAuthenticationService authentication, ProxyAdministrationDispatcher operations) : ApplicationControl.ApplicationControlBase, IDisposable
{
    private readonly SemaphoreSlim _admission = new(16, 16);

    public override async Task<ControlReply> Execute(ControlRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!await _admission.WaitAsync(0, context.CancellationToken).ConfigureAwait(false)) return new ControlReply { StatusCode = 429 };
        try { return await ExecuteAuthorizedAsync(request, context.CancellationToken).ConfigureAwait(false); }
        finally { _admission.Release(); }
    }

    private async Task<ControlReply> ExecuteAuthorizedAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        if (request.Version != 1 || !Enum.IsDefined(request.Operation) || request.Operation == ControlOperation.ControlUnspecified ||
            request.JsonPayload.Length > 256 * 1024 || request.AdministratorCredential.Length > 512 ||
            !IPAddress.TryParse(request.PeerAddress, out _) || !Guid.TryParseExact(request.RequestId, "N", out var id) || id == Guid.Empty)
            return new ControlReply { StatusCode = 400, SafeReason = "Control envelope is invalid." };
        foreach (var character in request.AdministratorCredential)
            if (char.IsControl(character)) return new ControlReply { StatusCode = 400, SafeReason = "Control credential is invalid." };
        var input = new ProxyAdminRequestAuthenticationInput("CONTROL", "/admin/" + request.Operation, request.PeerAddress,
            new ProxyAdminPresentedCredentials([request.AdministratorCredential], []));
        var outcome = authentication.Authenticate(input);
        if (!outcome.Allowed) return new ControlReply { StatusCode = checked((uint)(outcome.DeniedStatusCode ?? 403)), SafeReason = "Administrator authentication failed." };
        try
        {
            var reply = await operations.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            authentication.RecordOperationCompleted(input, outcome, checked((int)reply.StatusCode), succeeded: reply.StatusCode < 400);
            return reply;
        }
        catch
        {
            authentication.RecordFailed(input, outcome);
            throw;
        }
    }

    public void Dispose() => _admission.Dispose();

    public override Task<PresentationPlan> GatewayPlan(GatewayIdentity request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (plans is null) throw new RpcException(new Status(StatusCode.Unavailable, "Serving plan is unavailable."));
        if (request.Version != 1) throw new RpcException(new Status(StatusCode.InvalidArgument, "Unsupported plan protocol."));
        try { return Task.FromResult(plans.Read(request.GatewayId)); }
        catch (UnauthorizedAccessException) { throw new RpcException(new Status(StatusCode.PermissionDenied, "Unrecognized Gateway identity.")); }
    }

    public override Task<ControlReply> AcknowledgePlan(PlanAcknowledgment request, ServerCallContext context)
    {
        if (plans is null) throw new RpcException(new Status(StatusCode.Unavailable, "Serving plan is unavailable."));
        try
        {
            var applied = plans.Acknowledge(request);
            return Task.FromResult(new ControlReply { StatusCode = applied ? 200u : 409u, SafeReason = applied ? "Plan applied." : "Gateway did not apply its plan." });
        }
        catch (InvalidDataException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Plan acknowledgment does not match.")); }
    }
}
