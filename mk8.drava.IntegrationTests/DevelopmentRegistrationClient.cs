using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Grpc.Net.Client;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentRegistrationClient : IDisposable
{
    private readonly DevelopmentSiteClient _http;
    private readonly GrpcChannel _channel;
    private readonly X509Certificate2 _certificate;
    private readonly ServiceRegistry.ServiceRegistryClient _client;

    public DevelopmentRegistrationClient(TwoProcessProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        _http = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.RegistrationPort, "register.site.test", proxy.NodeCertificatePath);
        _channel = GrpcChannel.ForAddress(_http.Client.BaseAddress!, new GrpcChannelOptions { HttpClient = _http.Client });
        _certificate = X509CertificateLoader.LoadPkcs12FromFile(proxy.NodeCertificatePath, password: null, X509KeyStorageFlags.EphemeralKeySet);
        _client = new ServiceRegistry.ServiceRegistryClient(_channel);
    }

    public async Task<RegistrationStatus> SubmitAsync(RegistrationCommand command, CancellationToken cancellationToken)
    {
        using var challengeCall = _client.ChallengeAsync(new ChallengeRequest { Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(_certificate.RawData) }, cancellationToken: cancellationToken);
        var challenge = await challengeCall.ResponseAsync.ConfigureAwait(false);
        var payload = RegistrationJson.Encode(command);
        using var key = _certificate.GetECDsaPrivateKey() ?? throw new InvalidOperationException("Development enrollment has no signing key.");
        using var submit = _client.SubmitAsync(new SignedCommand
        {
            Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(_certificate.RawData), Nonce = challenge.Nonce, JsonPayload = ByteString.CopyFrom(payload),
            Signature = ByteString.CopyFrom(RegistrationProof.Sign(key, challenge.SiteId, 1, challenge.Nonce.Span, payload)),
        }, cancellationToken: cancellationToken);
        return RegistrationJson.DecodeStatus((await submit.ResponseAsync.ConfigureAwait(false)).JsonPayload.Span);
    }

    public void Dispose() { _channel.Dispose(); _http.Dispose(); _certificate.Dispose(); }
}
