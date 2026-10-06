using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Mk8.Drava.Application.BLL.NodeRelay;
using Mk8.Drava.Application.DAL.Administration;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Application.Transport;

internal sealed class NodeAgentLocalService(NodeRelayMappings mappings, NodeAgentDescriptorState state, NodeAgentBootstrap bootstrap) : NodeAgentLocal.NodeAgentLocalBase
{
    private readonly byte[] _tokenHash = SHA256.HashData(Encoding.ASCII.GetBytes(AdministratorCredentialFile.Read(bootstrap.LocalListen.IdentityTokenPath)));

    public override Task<NodeLocalReply> Apply(NodeLocalRequest request, ServerCallContext context)
    {
        if (context.GetHttpContext().Request.IsHttps) throw new RpcException(new Status(StatusCode.PermissionDenied, "Local mappings require the private node socket."));
        var credentials = context.RequestHeaders.Where(static entry => string.Equals(entry.Key, "authorization", StringComparison.Ordinal)).ToArray();
        if (credentials.Length != 1 || credentials[0].Value.Length is < 39 or > 263 || !credentials[0].Value.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.ASCII.GetBytes(credentials[0].Value[7..])), _tokenHash))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Private node identity is required."));
        var descriptor = state.Read() ?? throw new RpcException(new Status(StatusCode.Unavailable, "Node agent is starting."));
        if (request.Version != 1) throw new RpcException(new Status(StatusCode.InvalidArgument, "Unsupported local mapping version."));
        try
        {
            var command = RegistrationJson.Decode(request.CommandJson.Memory);
            if (command.Version != 1) throw new InvalidDataException("Unsupported mapping command.");
            if (command.Operation == RegistrationOperation.Drain)
            {
                if (command.Advertisement is not null) throw new InvalidDataException("Drain cannot change an endpoint.");
                mappings.Drain(command.Identity);
            }
            else if (command.Operation is RegistrationOperation.Register or RegistrationOperation.Renew)
            {
                var advertisement = command.Advertisement ?? throw new InvalidDataException("Local renewal requires its bound endpoint.");
                var relay = new NodeRelayEndpoint { AgentBootId = descriptor.AgentBootId, Address = descriptor.RelayAddress, Port = descriptor.RelayPort, CertificateFingerprint = descriptor.CertificateFingerprint };
                if (advertisement.Relay is not null && advertisement.Relay != relay) throw new UnauthorizedAccessException("Mapping belongs to another agent boot.");
                mappings.Renew(command.Identity, advertisement with { Relay = relay });
            }
            else throw new InvalidDataException("Unknown local mapping operation.");
            return Task.FromResult(new NodeLocalReply { StatusCode = 200, DescriptorJson = ByteString.CopyFromUtf8(BootstrapFile.Serialize(descriptor)) });
        }
        catch (UnauthorizedAccessException) { throw new RpcException(new Status(StatusCode.PermissionDenied, "Local mapping is outside enrollment scope.")); }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        { throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid local mapping contract.")); }
    }
}
