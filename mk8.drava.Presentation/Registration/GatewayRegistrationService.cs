using System.Security.Cryptography;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Presentation.Registration;

public sealed class GatewayRegistrationService(ApplicationChannel channel, GatewayBootstrap bootstrap) : ServiceRegistry.ServiceRegistryBase
{
    public override async Task<ChallengeReply> Challenge(ChallengeRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ValidatePeerAsync(request.EnrollmentCertificateDer.Memory, context).ConfigureAwait(false);
        var client = new ServiceRegistry.ServiceRegistryClient(channel.Invoker);
        using var call = client.ChallengeAsync(request, channel.Credentials, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: context.CancellationToken);
        return await call.ResponseAsync.ConfigureAwait(false);
    }

    public override async Task<RegistrationReply> Submit(SignedCommand request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ValidatePeerAsync(request.EnrollmentCertificateDer.Memory, context).ConfigureAwait(false);
        if (request.JsonPayload.Length > RegistrationProof.MaximumPayloadBytes || request.Nonce.Length != 32 || request.Signature.Length is < 64 or > 80)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Registration command exceeds its bounds."));
        var client = new ServiceRegistry.ServiceRegistryClient(channel.Invoker);
        using var call = client.SubmitAsync(request, channel.Credentials, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: context.CancellationToken);
        return await call.ResponseAsync.ConfigureAwait(false);
    }

    private async ValueTask ValidatePeerAsync(ReadOnlyMemory<byte> enrollmentCertificate, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.GetHttpContext();
        if (http.Connection.LocalPort != bootstrap.RegistrationPort || !http.Request.IsHttps || enrollmentCertificate.Length is < 128 or > RegistrationProof.MaximumCertificateBytes)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Registration requires its site TLS listener."));
        var certificate = await http.Connection.GetClientCertificateAsync(context.CancellationToken).ConfigureAwait(false);
        if (certificate is null || !CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), SHA256.HashData(enrollmentCertificate.Span)))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Submitted enrollment differs from the TLS peer."));
    }
}
