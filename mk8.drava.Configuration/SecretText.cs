using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Mk8.Drava.Configuration;

// Values are serialized only into the installation-local protected collection.
[JsonConverter(typeof(SecretTextJsonConverter))]
public sealed class SecretText : IEquatable<SecretText>
{
    private readonly string _value;
    public static SecretText Empty { get; } = new("");

    public SecretText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 1024 * 1024) throw new ArgumentException("Secret exceeds its value bound.", nameof(value));
        _value = value;
    }

    public bool IsEmpty => _value.Length == 0;
    public string Reveal() => _value;

    public string RequireToken()
    {
        if (_value.Length is < 32 or > 256 || _value.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
            throw new InvalidDataException("Invalid scoped identity encoding.");
        return _value;
    }

    public string RequirePrivateP256Key()
    {
        if (_value.Length is < 128 or > 65536 || _value.Any(static character => !char.IsAscii(character)))
            throw new InvalidDataException("ACME account requires a bounded private P256 key.");
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(_value);
            if (!string.Equals(key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value, "1.2.840.10045.3.1.7", StringComparison.Ordinal))
                throw new InvalidDataException("ACME account requires its configured private P256 key.");
            Span<byte> privateProof = stackalloc byte[512];
            try
            {
                if (!key.TryExportPkcs8PrivateKey(privateProof, out _)) throw new InvalidDataException("ACME account private key exceeds its P256 bound.");
            }
            finally { CryptographicOperations.ZeroMemory(privateProof); }
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        { throw new InvalidDataException("The configured ACME private account key is invalid.", exception); }
        return _value;
    }

    public bool Equals(SecretText? other) => other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is SecretText other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);
    public override string ToString() => "[redacted]";
}
