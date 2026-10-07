using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentAcmeServer : HttpMessageHandler
{
    private readonly DevelopmentAcmeDnsProvider _dns;
    private readonly string _accountPath;
    private readonly X509Certificate2 _root;
    private readonly X509Certificate2 _intermediate;
    private readonly HashSet<int> _validated = [];
    private readonly List<string> _domains = [];
    private ECDsa? _account;
    private string _thumbprint = "";
    private string? _certificate;
    private int _nonce;
    public bool CorruptOrder { get; init; }
    public bool CorruptAuthorization { get; init; }
    public bool RejectAuthorization { get; init; }
    public bool PendingOrder { get; init; }
    public int Requests { get; private set; }
    public int Validations { get; private set; }
    public int Finalizations { get; private set; }
    public string AccountPublicKey { get; private set; } = "";
    public byte[] RootCertificate { get; }

    public DevelopmentAcmeServer(DevelopmentAcmeDnsProvider dns, string accountPath)
    {
        _dns = dns; _accountPath = accountPath;
        var now = DateTimeOffset.UtcNow;
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = Authority("CN=development-acme-root", rootKey, 1);
        _root = rootRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));
        RootCertificate = _root.RawData;
        try
        {
            using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var certificate = Authority("CN=development-acme-intermediate", intermediateKey, 0)
                .Create(_root, now.AddMinutes(-4), now.AddMonths(6), RandomNumberGenerator.GetBytes(16));
            _intermediate = certificate.CopyWithPrivateKey(intermediateKey);
        }
        catch { _root.Dispose(); throw; }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Requests++;
        if (!File.Exists(_accountPath)) throw new InvalidDataException("Account key was not persisted before ACME network access.");
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && string.Equals(path, "/directory", StringComparison.Ordinal))
            return Reply(new { newNonce = Endpoint("/nonce"), newAccount = Endpoint("/account"), newOrder = Endpoint("/new-order") });
        if (request.Method == HttpMethod.Head && string.Equals(path, "/nonce", StringComparison.Ordinal)) return Reply(new { });
        if (request.Method != HttpMethod.Post) throw new InvalidDataException("Unexpected development ACME method.");
        using var jws = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        VerifySignature(jws.RootElement, request.RequestUri);
        var encoded = jws.RootElement.GetProperty("payload").GetString()!;
        using var payload = JsonDocument.Parse(encoded.Length == 0 ? "{}"u8.ToArray() : Decode(encoded));
        if (string.Equals(path, "/account", StringComparison.Ordinal))
            return Reply(new { status = "valid", orders = Endpoint("/orders") }, "/account");
        return RespondToOperation(path, payload.RootElement);
    }

    private HttpResponseMessage RespondToOperation(string path, JsonElement payload)
    {
        if (string.Equals(path, "/new-order", StringComparison.Ordinal))
        {
            _domains.Clear(); _validated.Clear(); _certificate = null;
            foreach (var identifier in payload.GetProperty("identifiers").EnumerateArray()) _domains.Add(identifier.GetProperty("value").GetString()!);
            return Reply(Order(), "/order");
        }
        if (string.Equals(path, "/order", StringComparison.Ordinal)) return Reply(Order());
        if (path.StartsWith("/auth/", StringComparison.Ordinal)) return Reply(Authorization(Index(path)));
        if (path.StartsWith("/challenge/", StringComparison.Ordinal))
        {
            var index = Index(path);
            var identifier = _domains[index].StartsWith("*.", StringComparison.Ordinal) ? _domains[index][2..] : _domains[index];
            var digest = Encode(SHA256.HashData(Encoding.ASCII.GetBytes(Token(index) + "." + _thumbprint)));
            if (!_dns.Records.Any(r => string.Equals(r.Host, "_acme-challenge." + identifier, StringComparison.Ordinal) && string.Equals(r.Value, digest, StringComparison.Ordinal)) || _dns.Proofs == 0)
                throw new InvalidDataException("ACME was notified before challenge publication and propagation proof.");
            Validations++; _validated.Add(index);
            return Reply(new { type = "dns-01", url = Endpoint(path), status = RejectAuthorization ? "invalid" : "valid", token = Token(index) });
        }
        if (string.Equals(path, "/finalize", StringComparison.Ordinal))
        {
            if (_validated.Count != _domains.Count) throw new InvalidDataException("CSR finalized before authorization completed.");
            SignRequest(Decode(payload.GetProperty("csr").GetString()!)); Finalizations++;
            return Reply(Order());
        }
        if (string.Equals(path, "/certificate", StringComparison.Ordinal) && _certificate is not null)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_certificate, Encoding.ASCII, "application/pem-certificate-chain") };
            response.Headers.Add("Replay-Nonce", NextNonce());
            return response;
        }
        throw new InvalidDataException("Unknown development ACME endpoint.");
    }

    private object Order() => new
    {
        status = _certificate is not null ? "valid" : !PendingOrder && _validated.Count == _domains.Count ? "ready" : "pending",
        identifiers = _domains.Select(domain => new { type = "dns", value = CorruptOrder ? "foreign.example" : domain }).ToArray(),
        authorizations = Enumerable.Range(0, _domains.Count).Select(index => Endpoint("/auth/" + index.ToString(CultureInfo.InvariantCulture))).ToArray(),
        finalize = Endpoint("/finalize"), certificate = _certificate is null ? null : Endpoint("/certificate"),
    };

    private object Authorization(int index) => new
    {
        status = _validated.Contains(index) ? RejectAuthorization ? "invalid" : "valid" : "pending",
        identifier = new { type = "dns", value = CorruptAuthorization ? "foreign.example" : _domains[index].TrimStart('*', '.') },
        wildcard = _domains[index].StartsWith("*.", StringComparison.Ordinal),
        expires = DateTimeOffset.UtcNow.AddDays(1),
        challenges = new[] { new { type = "dns-01", url = Endpoint("/challenge/" + index.ToString(CultureInfo.InvariantCulture)), token = Token(index), status = "pending" } },
    };

    private void VerifySignature(JsonElement jws, Uri uri)
    {
        var encoded = jws.GetProperty("protected").GetString()!;
        using var header = JsonDocument.Parse(Decode(encoded));
        if (!string.Equals(header.RootElement.GetProperty("url").GetString(), uri.AbsoluteUri, StringComparison.Ordinal) ||
            !string.Equals(header.RootElement.GetProperty("alg").GetString(), "ES256", StringComparison.Ordinal))
            throw new InvalidDataException("Development ACME received an invalid protected request.");
        if (header.RootElement.TryGetProperty("jwk", out var jwk))
        {
            var parameters = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = Decode(jwk.GetProperty("x").GetString()!), Y = Decode(jwk.GetProperty("y").GetString()!) } };
            var identity = Convert.ToHexString(SHA256.HashData(parameters.Q.X!.Concat(parameters.Q.Y!).ToArray()));
            if (AccountPublicKey.Length != 0 && !string.Equals(identity, AccountPublicKey, StringComparison.Ordinal)) throw new InvalidDataException("ACME account key changed.");
            AccountPublicKey = identity;
            var canonical = JsonSerializer.Serialize(new { crv = "P-256", kty = "EC", x = jwk.GetProperty("x").GetString(), y = jwk.GetProperty("y").GetString() });
            _thumbprint = Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            _account?.Dispose(); _account = ECDsa.Create(parameters);
        }
        if (_account is null || !_account.VerifyData(Encoding.ASCII.GetBytes(encoded + "." + jws.GetProperty("payload").GetString()), Decode(jws.GetProperty("signature").GetString()!), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new InvalidDataException("Development ACME request signature is invalid.");
    }

    private void SignRequest(byte[] bytes)
    {
        var request = CertificateRequest.LoadSigningRequest(bytes, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        var extensions = request.CertificateExtensions.OfType<X509SubjectAlternativeNameExtension>().ToArray();
        if (extensions.Length != 1) throw new InvalidDataException("ACME CSR requires exactly one DNS SAN extension.");
        var names = extensions[0].EnumerateDnsNames().ToArray();
        if (!names.Order(StringComparer.Ordinal).SequenceEqual(_domains.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("ACME CSR contains unapproved names.");
        request.CertificateExtensions.Clear();
        var approvedNames = new SubjectAlternativeNameBuilder();
        foreach (var name in names) approvedNames.AddDnsName(name);
        request.CertificateExtensions.Add(approvedNames.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        var now = DateTimeOffset.UtcNow;
        using var leaf = request.Create(_intermediate, now.AddMinutes(-3), now.AddDays(7), RandomNumberGenerator.GetBytes(16));
        _certificate = leaf.ExportCertificatePem() + "\n" + _intermediate.ExportCertificatePem();
    }

    private HttpResponseMessage Reply(object payload, string? location = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        response.Headers.Add("Replay-Nonce", NextNonce());
        if (location is not null) response.Headers.Location = new Uri(Endpoint(location));
        return response;
    }

    private string NextNonce() => Convert.ToBase64String(Encoding.ASCII.GetBytes((++_nonce).ToString(CultureInfo.InvariantCulture))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Endpoint(string path) => "https://ca.example" + path;
    private static string Token(int index) => "development_acme_token_" + index.ToString(CultureInfo.InvariantCulture);
    private static int Index(string path) => int.Parse(path.AsSpan(path.LastIndexOf('/') + 1), CultureInfo.InvariantCulture);
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
    private static CertificateRequest Authority(string name, ECDsa key, int pathLength)
    {
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, pathLength, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _account?.Dispose(); _intermediate.Dispose(); _root.Dispose(); }
        base.Dispose(disposing);
    }
}
