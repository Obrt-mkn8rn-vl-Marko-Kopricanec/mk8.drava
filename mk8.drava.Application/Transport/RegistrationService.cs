using Google.Protobuf;
using Grpc.Core;
using Mk8.Drava.Application.INF.Registry;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Application.Transport;

internal sealed class RegistrationService(SignedRegistrationHandler handler) : ServiceRegistry.ServiceRegistryBase
{
    public override Task<ChallengeReply> Challenge(ChallengeRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try { return Task.FromResult(handler.Challenge(request)); }
        catch (UnauthorizedAccessException) { throw new RpcException(new Status(StatusCode.Unauthenticated, "Enrollment is invalid or revoked.")); }
        catch (System.Security.Cryptography.CryptographicException) { throw new RpcException(new Status(StatusCode.Unauthenticated, "Enrollment certificate is malformed.")); }
        catch (InvalidDataException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid enrollment challenge.")); }
        catch (InvalidOperationException) { throw new RpcException(new Status(StatusCode.ResourceExhausted, "Enrollment challenge quota exceeded.")); }
    }

    public override async Task<RegistrationReply> Submit(SignedCommand request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var status = await handler.SubmitAsync(request, context.CancellationToken).ConfigureAwait(false);
            return new RegistrationReply { StatusCode = 200, JsonPayload = ByteString.CopyFrom(RegistrationJson.EncodeStatus(status)) };
        }
        catch (UnauthorizedAccessException) { throw new RpcException(new Status(StatusCode.PermissionDenied, "Registration proof or scope is invalid.")); }
        catch (System.Security.Cryptography.CryptographicException) { throw new RpcException(new Status(StatusCode.PermissionDenied, "Enrollment certificate is malformed.")); }
        catch (InvalidDataException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid registration command or instance state.")); }
        catch (System.Text.Json.JsonException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Malformed registration command.")); }
    }
}
