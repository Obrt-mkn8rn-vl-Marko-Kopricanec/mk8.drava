using System.Net.Security;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Grpc.Net.Client;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Transport.Clients;

public sealed class ApplicationChannel : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly X509Certificate2? _clientCertificate;
    private readonly X509Certificate2? _trustedCertificate;
    private readonly string _identityToken;

    public ApplicationChannel(IpcEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.Validate();
        _identityToken = ReadIdentity(endpoint.IdentityTokenPath);
        var handler = CreateHandler(endpoint);
        try
        {
            if (endpoint.HttpsAddress.Length > 0)
            {
                _clientCertificate = X509CertificateLoader.LoadPkcs12FromFile(endpoint.ClientCertificatePath, password: null, X509KeyStorageFlags.EphemeralKeySet);
                _trustedCertificate = X509CertificateLoader.LoadCertificateFromFile(endpoint.TrustedCertificatePath);
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection { _clientCertificate },
                    RemoteCertificateValidationCallback = ValidateServerCertificate,
                };
            }
            _channel = GrpcChannel.ForAddress(endpoint.HttpsAddress.Length == 0 ? "http://localhost" : endpoint.HttpsAddress, new GrpcChannelOptions
            {
                HttpHandler = handler, DisposeHttpClient = true,
                MaxReceiveMessageSize = 8 * 1024 * 1024, MaxSendMessageSize = 8 * 1024 * 1024,
            });
        }
        catch
        {
            handler.Dispose();
            _clientCertificate?.Dispose();
            _trustedCertificate?.Dispose();
            throw;
        }
    }

    private static string ReadIdentity(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 32 or > 256) throw new InvalidDataException("Scoped transport identity is absent or invalid.");
        var token = File.ReadAllText(path).Trim();
        if (token.Length is < 32 or > 256 || token.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_')) throw new InvalidDataException("Invalid scoped transport identity encoding.");
        return token;
    }

    private static SocketsHttpHandler CreateHandler(IpcEndpoint endpoint)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            AllowAutoRedirect = false,
        };
        if (endpoint.UnixSocketPath.Length > 0)
        {
            handler.ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.UnixSocketPath), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }
        else if (endpoint.NamedPipeName.Length > 0)
        {
            handler.ConnectCallback = async (_, cancellationToken) =>
            {
                var pipe = new NamedPipeClientStream(".", endpoint.NamedPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return pipe;
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            };
        }
        return handler;
    }

    public CallInvoker Invoker => _channel.CreateCallInvoker();
    public Metadata Credentials => new() { { "authorization", "Bearer " + _identityToken } };

    public void Dispose()
    {
        _channel.Dispose();
        _clientCertificate?.Dispose();
        _trustedCertificate?.Dispose();
    }

    private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null || _trustedCertificate is null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != SslPolicyErrors.None) return false;
        using var candidate = new X509Certificate2(certificate);
        using var verifier = new X509Chain();
        verifier.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        verifier.ChainPolicy.CustomTrustStore.Add(_trustedCertificate);
        verifier.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        verifier.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        verifier.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
        return verifier.Build(candidate);
    }
}
