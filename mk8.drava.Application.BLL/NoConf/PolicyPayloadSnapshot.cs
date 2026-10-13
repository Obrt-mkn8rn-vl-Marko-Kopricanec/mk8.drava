using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

namespace Mk8.Drava.Application.BLL.NoConf;

public sealed class PolicyPayloadSnapshot
{
    public const int MaximumPayloads = 65; // All64 committed payloads plus one unreachable stage.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly FrozenDictionary<string, string> _payloads;
    private readonly FrozenDictionary<string, PolicyPayloadReference> _references;

    public PolicyPayloadSnapshot(string siteId, IEnumerable<KeyValuePair<string, string>> payloads)
    {
        ArgumentNullException.ThrowIfNull(siteId);
        ArgumentNullException.ThrowIfNull(payloads);
        if (siteId.Length is < 1 or > 128) throw new InvalidDataException("Policy archive requires a bounded site identity.");
        try { _ = StrictUtf8.GetByteCount(siteId); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Policy archive site identity requires valid UTF8.", exception); }
        SiteId = siteId;
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        var references = new Dictionary<string, PolicyPayloadReference>(StringComparer.Ordinal);
        foreach (var payload in payloads)
        {
            var reference = Describe(payload.Value);
            if (!string.Equals(reference.Digest, payload.Key, StringComparison.Ordinal) || !captured.TryAdd(payload.Key, payload.Value))
                throw new InvalidDataException("Policy archive has a duplicate or damaged payload.");
            references.Add(reference.Digest, reference);
            if (captured.Count > MaximumPayloads) throw new InvalidDataException("Policy archive exceeds its payload count bound.");
        }
        _payloads = captured.ToFrozenDictionary(StringComparer.Ordinal);
        _references = references.ToFrozenDictionary(StringComparer.Ordinal);
        References = Array.AsReadOnly(references.Values.OrderBy(static reference => reference.Digest, StringComparer.Ordinal).ToArray());
    }

    public string SiteId { get; }
    public int SchemaVersion { get; } = 1;
    public IReadOnlyList<PolicyPayloadReference> References { get; }

    public string Resolve(PolicyPayloadReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!_references.TryGetValue(reference.Digest, out var actual) || actual.Utf8Bytes != reference.Utf8Bytes)
            throw new InvalidDataException("Referenced policy payload is missing or has an invalid byte count.");
        return _payloads[reference.Digest];
    }

    public static PolicyPayloadReference Describe(string canonicalJson)
    {
        ArgumentNullException.ThrowIfNull(canonicalJson);
        if (canonicalJson.Length > PolicyPayloadReference.MaximumUtf8Bytes) throw new InvalidDataException("Policy payload exceeds its durable byte bound.");
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(canonicalJson); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Policy payload requires valid UTF8.", exception); }
        try { return new PolicyPayloadReference(Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
