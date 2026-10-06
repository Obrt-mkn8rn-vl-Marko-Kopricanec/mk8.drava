using Grpc.Core;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.Transport;

internal sealed class ControlService(ServingPlanState plans) : ApplicationControl.ApplicationControlBase
{
    public override Task<PresentationPlan> GatewayPlan(GatewayIdentity request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != 1) throw new RpcException(new Status(StatusCode.InvalidArgument, "Unsupported plan protocol."));
        try { return Task.FromResult(plans.Read(request.GatewayId)); }
        catch (UnauthorizedAccessException) { throw new RpcException(new Status(StatusCode.PermissionDenied, "Unrecognized Gateway identity.")); }
    }

    public override Task<ControlReply> AcknowledgePlan(PlanAcknowledgment request, ServerCallContext context)
    {
        try
        {
            var applied = plans.Acknowledge(request);
            return Task.FromResult(new ControlReply { StatusCode = applied ? 200u : 409u, SafeReason = applied ? "Plan applied." : "Gateway did not apply its plan." });
        }
        catch (InvalidDataException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Plan acknowledgment does not match.")); }
    }
}
