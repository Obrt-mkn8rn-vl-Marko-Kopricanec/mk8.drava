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

    public bool Equals(SecretText? other) => other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is SecretText other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);
    public override string ToString() => "[redacted]";
}
