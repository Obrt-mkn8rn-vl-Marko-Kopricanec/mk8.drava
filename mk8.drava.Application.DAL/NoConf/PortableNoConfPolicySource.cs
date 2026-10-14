using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.DAL.NoConf;

public sealed class PortableNoConfPolicySource : INoConfPolicySource
{
    private const int MaximumEnvelopeBytes = 512 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly PortableRole<ApplicationBootstrap> _role;
    private readonly string _siteId;

    public PortableNoConfPolicySource(PortableRole<ApplicationBootstrap> role)
    {
        ArgumentNullException.ThrowIfNull(role);
        _role = role;
        _siteId = role.Configuration.SiteId;
        _ = new PolicyPayloadSnapshot(_siteId, []);
    }

    public async ValueTask<NoConfPolicy?> ReadAsync(CancellationToken cancellationToken)
    {
        var documents = await _role.RefreshPolicyDocumentsAsync(cancellationToken).ConfigureAwait(false);
        if (documents.ConfiguredSource.IsEmpty) return null;
        return Parse(documents.ConfiguredSource, _siteId);
    }

    public static SecretText Encode(NoConfPolicy policy, string siteId)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _ = new PolicyPayloadSnapshot(siteId, []);
        var bytes = StrictUtf8.GetBytes(NoConfPolicyFile.Encode(policy));
        try
        {
            return new SecretText(JsonSerializer.Serialize(new { schemaVersion = 1, siteId, policy = EncodeBytes(bytes) }));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static NoConfPolicy Parse(SecretText source, string siteId)
    {
        try
        {
            var text = source.Reveal();
            if (text.Length > MaximumEnvelopeBytes || StrictUtf8.GetByteCount(text) > MaximumEnvelopeBytes)
                throw new InvalidDataException("Protected configured policy exceeds its envelope bound.");
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            RequireEnvelope(root, siteId);
            var encoded = root.GetProperty("policy").GetString() ?? throw new InvalidDataException("Protected configured policy is absent.");
            if (encoded.Length > 349526 || encoded.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
                throw new InvalidDataException("Protected configured policy exceeds its canonical encoding bound.");
            var bytes = Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight((encoded.Length + 3) / 4 * 4, '='));
            try
            {
                if (bytes.Length is < 2 or > PolicyPayloadReference.MaximumUtf8Bytes || !string.Equals(EncodeBytes(bytes), encoded, StringComparison.Ordinal))
                    throw new InvalidDataException("Protected configured policy is not canonical base64url.");
                return NoConfPolicyFile.Parse(StrictUtf8.GetString(bytes));
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception exception) when (exception is JsonException or FormatException or DecoderFallbackException or EncoderFallbackException)
        {
            throw new InvalidDataException("Protected configured policy is malformed.", exception);
        }
    }

    private static void RequireEnvelope(JsonElement root, string siteId)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Protected configured policy requires an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("schemaVersion" or "siteId" or "policy") || !names.Add(property.Name))
                throw new InvalidDataException("Protected configured policy has unknown or duplicate fields.");
        }

        if (names.Count != 3 || root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number
            || !root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1
            || root.GetProperty("siteId").ValueKind != JsonValueKind.String || !string.Equals(root.GetProperty("siteId").GetString(), siteId, StringComparison.Ordinal)
            || root.GetProperty("policy").ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Protected configured policy schema or site identity is invalid.");
        }
    }

    private static string EncodeBytes(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
