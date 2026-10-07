using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Grpc.Net.Client;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Discovery;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Transport.Registration;

public sealed class SiteRegistrationChannel : IDisposable
{
    private readonly RegistrationSiteTrust _trust;
    private readonly X509Certificate2 _root;
    private readonly X509Certificate2 _node;
    private readonly HttpClient _http;
    private readonly SocketsHttpHandler _handler;
    private readonly GrpcChannel _channel;
    private readonly ServiceRegistry.ServiceRegistryClient _client;
    private IPAddress? _localAddress;
    private ChallengeReply? _verifiedChallenge;

    public SiteRegistrationChannel(RegistrationSiteTrust trust, DiscoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(candidate);
        trust.Validate();
        _trust = trust;
        ValidateFile(trust.NodeCertificatePath, privateMaterial: true);
        ValidateFile(trust.RootCertificatePath, privateMaterial: false);
        _root = X509CertificateLoader.LoadCertificateFromFile(trust.RootCertificatePath);
        try
        {
            _node = X509CertificateLoader.LoadPkcs12FromFile(trust.NodeCertificatePath, password: null, X509KeyStorageFlags.EphemeralKeySet);
            if (!string.Equals(_root.GetCertHashString(HashAlgorithmName.SHA256), trust.RootFingerprint, StringComparison.Ordinal) || !_node.HasPrivateKey)
                throw new InvalidDataException("Enrollment root pin or node key does not match.");
            _handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(3),
                MaxConnectionsPerServer = 1, MaxResponseHeadersLength = 16,
                ConnectCallback = (_, token) => ConnectAsync(candidate, token),
                SslOptions = new SslClientAuthenticationOptions { ClientCertificates = new X509CertificateCollection { _node }, RemoteCertificateValidationCallback = ValidateServer },
            };
            _http = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            _channel = GrpcChannel.ForAddress($"https://register.{trust.Domain}:{candidate.Port}", new GrpcChannelOptions { HttpClient = _http, MaxReceiveMessageSize = 64 * 1024, MaxSendMessageSize = 64 * 1024 });
            _client = new ServiceRegistry.ServiceRegistryClient(_channel);
        }
        catch { _handler?.Dispose(); _http?.Dispose(); _root.Dispose(); _node?.Dispose(); throw; }
    }

    public IPAddress? LocalAddress => Volatile.Read(ref _localAddress);
    public string NodeCertificateFingerprint => _node.GetCertHashString(HashAlgorithmName.SHA256);

    public async ValueTask VerifySiteAsync(CancellationToken cancellationToken)
    {
        var challenge = await ChallengeAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _verifiedChallenge, challenge);
    }

    private async ValueTask<ChallengeReply> ChallengeAsync(CancellationToken cancellationToken)
    {
        using var call = _client.ChallengeAsync(new ChallengeRequest { Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(_node.RawData) },
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: cancellationToken);
        var challenge = await call.ResponseAsync.ConfigureAwait(false);
        if (challenge.Version != 1 || challenge.Nonce.Length != 32 || !string.Equals(challenge.SiteId, _trust.SiteId, StringComparison.Ordinal))
            throw new InvalidDataException("Discovered Gateway did not prove the enrolled registration site.");
        return challenge;
    }

    public async ValueTask<RegistrationStatus> SubmitAsync(RegistrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Identity is null || !string.Equals(command.Identity.SiteId, _trust.SiteId, StringComparison.Ordinal)) throw new InvalidDataException("Registration belongs to another site.");
        var challenge = Interlocked.Exchange(ref _verifiedChallenge, null);
        if (challenge is null || challenge.ExpiresUnixSeconds <= DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeSeconds()) challenge = await ChallengeAsync(cancellationToken).ConfigureAwait(false);
        var payload = RegistrationJson.Encode(command);
        using var key = _node.GetECDsaPrivateKey() ?? throw new InvalidDataException("Enrollment requires a P-256 signing key.");
        using var call = _client.SubmitAsync(new SignedCommand
        {
            Version = 1, EnrollmentCertificateDer = ByteString.CopyFrom(_node.RawData), Nonce = challenge.Nonce, JsonPayload = ByteString.CopyFrom(payload),
            Signature = ByteString.CopyFrom(RegistrationProof.Sign(key, challenge.SiteId, 1, challenge.Nonce.Span, payload)),
        }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: cancellationToken);
        var reply = await call.ResponseAsync.ConfigureAwait(false);
        if (reply.StatusCode != 200) throw new InvalidDataException("Registration was rejected.");
        var status = RegistrationJson.DecodeStatus(reply.JsonPayload.Span);
        if (status.Identity != command.Identity || !Enum.IsDefined(status.Phase) || status.AssignedUrls is null || status.AssignedUrls.Count > 16 || status.LeaseSeconds is < 15 or > 300 || status.RenewAfterSeconds is < 1 or > 60 || status.RenewAfterSeconds > status.LeaseSeconds / 3)
            throw new InvalidDataException("Registration response is inconsistent with the enrolled command.");
        if (status.Phase != RegistrationPhase.Ready && status.AssignedUrls.Count != 0) throw new InvalidDataException("Pending membership cannot supply an assigned URL.");
        if (status.Phase == RegistrationPhase.Ready && status.AssignedUrls.Count == 0) throw new InvalidDataException("Ready membership requires an assigned URL.");
        foreach (var assigned in status.AssignedUrls)
            if (assigned is null || assigned.Length > 2048 || !Uri.TryCreate(assigned, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https" ||
                !string.Equals(uri.DnsSafeHost, command.Identity.ServiceId + "." + _trust.Domain, StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidDataException("Assigned URL differs from the enrolled service.");
        return status;
    }

    private async ValueTask<Stream> ConnectAsync(DiscoveryCandidate candidate, CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(candidate.Address), candidate.Port);
        Socket? socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _localAddress, ((IPEndPoint?)socket.LocalEndPoint)?.Address);
            var stream = new NetworkStream(socket, ownsSocket: true);
            socket = null;
            return stream;
        }
        finally { socket?.Dispose(); }
    }

    private bool ValidateServer(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != SslPolicyErrors.None) return false;
        using var leaf = new X509Certificate2(certificate);
        if (!HasServerRole(leaf) || !leaf.MatchesHostname("register." + _trust.Domain, allowWildcards: false, allowCommonName: false)) return false;
        using var verifier = new X509Chain();
        verifier.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        verifier.ChainPolicy.CustomTrustStore.Add(_root);
        verifier.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        verifier.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        verifier.ChainPolicy.DisableCertificateDownloads = true;
        verifier.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return verifier.Build(leaf);
    }

    private static bool HasServerRole(X509Certificate2 certificate)
    {
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        var server = false;
        var leaf = false;
        if (certificate.RawData.Length > RegistrationProof.MaximumCertificateBytes || certificate.Extensions.Count > 64) return false;
        foreach (var extension in certificate.Extensions)
        {
            if (!extensions.Add(extension.Oid?.Value ?? "")) return false;
            if (extension is X509BasicConstraintsExtension basic)
            {
                if (basic.CertificateAuthority) return false;
                leaf = true;
            }
            if (extension is X509EnhancedKeyUsageExtension usage)
                foreach (var oid in usage.EnhancedKeyUsages)
                    if (string.Equals(oid.Value, "1.3.6.1.5.5.7.3.1", StringComparison.Ordinal)) server = true;
        }
        return server && leaf;
    }

    private static void ValidateFile(string path, bool privateMaterial)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length is < 1 or > 64 * 1024) throw new InvalidDataException("Enrollment file is missing, oversized or a symbolic link.");
        if (!privateMaterial || OperatingSystem.IsWindows()) return;
        var denied = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        var directory = file.Directory ?? throw new InvalidDataException("Enrollment directory is missing.");
        if (directory.LinkTarget is not null || (File.GetUnixFileMode(path) & denied) != UnixFileMode.None || (File.GetUnixFileMode(directory.FullName) & denied) != UnixFileMode.None)
            throw new InvalidDataException("Node enrollment requires a private file and directory.");
    }

    public void Dispose() { _channel.Dispose(); _http.Dispose(); _handler.Dispose(); _node.Dispose(); _root.Dispose(); }
}
