using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Registration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class PublicRegistrationTests
{
    [Fact]
    public async Task TlsEnrollmentRegistersThroughGatewayAndRejectsAReplayedProofAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("unused")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var http = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.RegistrationPort, "register.site.test", proxy.NodeCertificatePath);
        using var channel = GrpcChannel.ForAddress(http.Client.BaseAddress!, new GrpcChannelOptions { HttpClient = http.Client });
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(proxy.NodeCertificatePath, password: null, X509KeyStorageFlags.EphemeralKeySet);
        var registry = new ServiceRegistry.ServiceRegistryClient(channel);
        using var challengeCall = registry.ChallengeAsync(new ChallengeRequest { Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(certificate.RawData) });
        var challenge = await challengeCall.ResponseAsync.ConfigureAwait(true);
        Assert.Equal("development", challenge.SiteId);
        var command = new RegistrationCommand
        {
            Identity = new RegistrationIdentity { SiteId = "development", NodeId = "local", OwnerId = "development", ServiceId = "svc", ContractId = "v1", InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N") },
            Operation = RegistrationOperation.Register,
            Advertisement = new ServiceAdvertisement { DeploymentId = "development", Address = "127.0.0.1", Port = upstream.Port, ReadinessPath = "/ready" },
        };
        var signed = Sign(certificate, challenge, command);
        using var submit = registry.SubmitAsync(signed);
        var response = await submit.ResponseAsync.ConfigureAwait(true);
        var status = RegistrationJson.DecodeStatus(response.JsonPayload.Span);
        Assert.Equal(RegistrationPhase.Checking, status.Phase);
        Assert.Empty(status.AssignedUrls);
        Assert.Equal(command.Identity, status.Identity);
        using var replay = registry.SubmitAsync(signed);
        RpcException? rejected = null;
        try { await replay.ResponseAsync.ConfigureAwait(true); }
        catch (RpcException exception) { rejected = exception; }
        Assert.NotNull(rejected);
        Assert.Equal(StatusCode.PermissionDenied, rejected.StatusCode);
    }

    private static SignedCommand Sign(X509Certificate2 certificate, ChallengeReply challenge, RegistrationCommand command)
    {
        using var key = certificate.GetECDsaPrivateKey() ?? throw new InvalidOperationException("Enrollment fixture key is missing.");
        var payload = RegistrationJson.Encode(command);
        return new SignedCommand
        {
            Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(certificate.RawData), Nonce = challenge.Nonce,
            JsonPayload = ByteString.CopyFrom(payload), Signature = ByteString.CopyFrom(RegistrationProof.Sign(key, challenge.SiteId, 1, challenge.Nonce.Span, payload)),
        };
    }
}
