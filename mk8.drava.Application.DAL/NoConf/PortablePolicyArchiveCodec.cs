using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.DAL.NoConf;

internal static class PortablePolicyArchiveCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static PolicyPayloadSnapshot Read(PortablePolicyArchive archive, string siteId)
    {
        if (archive.IsEmpty) return new PolicyPayloadSnapshot(siteId, []);
        try
        {
            using var document = JsonDocument.Parse(archive.Reveal(), new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            RequireEnvelope(root, siteId);
            var payloads = root.GetProperty("payloads");
            if (payloads.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Policy archive requires a payload map.");
            var decoded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var payload in payloads.EnumerateObject())
            {
                if (decoded.Count >= PolicyPayloadSnapshot.MaximumPayloads || payload.Value.ValueKind != JsonValueKind.String
                    || !decoded.TryAdd(payload.Name, Decode(payload.Value.GetString() ?? throw new InvalidDataException("Policy archive payload is absent."))))
                {
                    throw new InvalidDataException("Policy archive has duplicate or excessive payloads.");
                }
            }
            return new PolicyPayloadSnapshot(siteId, decoded);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or DecoderFallbackException)
        {
            throw new InvalidDataException("Protected policy archive is malformed.", exception);
        }
    }

    internal static PortablePolicyArchive Encode(PolicyPayloadSnapshot snapshot)
    {
        var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in snapshot.References)
        {
            var bytes = StrictUtf8.GetBytes(snapshot.Resolve(reference));
            try { payloads.Add(reference.Digest, EncodeBytes(bytes)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        return new PortablePolicyArchive(JsonSerializer.Serialize(new { schemaVersion = snapshot.SchemaVersion, siteId = snapshot.SiteId, payloads }));
    }

    private static void RequireEnvelope(JsonElement root, string siteId)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Policy archive requires an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("schemaVersion" or "siteId" or "payloads") || !names.Add(property.Name))
                throw new InvalidDataException("Policy archive contains unknown or duplicate fields.");
        }
        if (names.Count != 3 || root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number
            || !root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1
            || root.GetProperty("siteId").ValueKind != JsonValueKind.String
            || !string.Equals(root.GetProperty("siteId").GetString(), siteId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Policy archive schema or site identity is invalid.");
        }
    }

    private static string Decode(string encoded)
    {
        if (encoded.Length > 349526 || encoded.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new InvalidDataException("Policy archive payload exceeds its canonical encoding bound.");
        var padded = encoded.Replace('-', '+').Replace('_', '/').PadRight((encoded.Length + 3) / 4 * 4, '=');
        var bytes = Convert.FromBase64String(padded);
        try
        {
            if (bytes.Length is < 2 or > PolicyPayloadReference.MaximumUtf8Bytes || !string.Equals(EncodeBytes(bytes), encoded, StringComparison.Ordinal))
                throw new InvalidDataException("Policy archive payload is not canonical base64url.");
            return StrictUtf8.GetString(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string EncodeBytes(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
