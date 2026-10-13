using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentSiteClient : IDisposable
{
    private readonly X509Certificate2 _root;
    private readonly SocketsHttpHandler _handler;
    private readonly X509Certificate2? _enrollment;
    private int _observedNoDelay = -1;
    private int _connectedSockets;
    public bool? ObservedTcpNoDelay => Volatile.Read(ref _observedNoDelay) switch { 0 => false, 1 => true, _ => null };
    public int ConnectedSockets => Volatile.Read(ref _connectedSockets);
    public HttpClient Client { get; }

    // Raw files here are exclusively disposable test-client inputs, never role startup fallbacks.
    public DevelopmentSiteClient(string rootPath, int port, string host, string? enrollmentPath = null, bool? tcpNoDelay = null)
        : this(ReadFixture(rootPath), port, host, enrollmentPath is null ? null : ReadFixture(enrollmentPath), tcpNoDelay) { }

    public DevelopmentSiteClient(SecretBytes root, int port, string host, SecretBytes? enrollment = null, bool? tcpNoDelay = null)
    {
        _root = PortableCertificate.LoadPublic(root);
        try
        {
            _enrollment = enrollment is null ? null : PortableCertificate.LoadPrivate(enrollment);
            _handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = ValidateServer },
                ConnectCallback = async (_, token) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        if (tcpNoDelay is { } noDelay) socket.NoDelay = noDelay;
                        Volatile.Write(ref _observedNoDelay, socket.NoDelay ? 1 : 0);
                        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), token).ConfigureAwait(false);
                        Interlocked.Increment(ref _connectedSockets);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                },
            };
            if (_enrollment is not null) _handler.SslOptions.ClientCertificates = [_enrollment];
            Client = new HttpClient(_handler, disposeHandler: false) { BaseAddress = new UriBuilder("https", host, port).Uri, Timeout = TimeSpan.FromSeconds(10), DefaultRequestVersion = HttpVersion.Version20, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
        }
        catch { _handler?.Dispose(); _enrollment?.Dispose(); _root.Dispose(); throw; }
    }

    private static SecretBytes ReadFixture(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new SecretBytes(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal bool ValidateServer(object sender, X509Certificate? certificate, X509Chain? existingChain, SslPolicyErrors errors)
    {
        if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != SslPolicyErrors.None) return false;
        using var leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(_root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
        return chain.Build(leaf);
    }

    public void Dispose() { Client.Dispose(); _handler.Dispose(); _enrollment?.Dispose(); _root.Dispose(); }
}
