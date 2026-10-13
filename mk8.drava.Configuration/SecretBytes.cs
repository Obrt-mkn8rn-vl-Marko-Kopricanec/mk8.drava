using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Mk8.Drava.Configuration;

// Owned copies prevent callers from mutating credentials held by the startup snapshot.
[JsonConverter(typeof(SecretBytesJsonConverter))]
public sealed class SecretBytes : IEquatable<SecretBytes>
{
    internal const int MaximumBytes = 1024 * 1024;
    private readonly byte[] _value;
    public static SecretBytes Empty { get; } = new([]);
    public int Length => _value.Length;
    internal ReadOnlySpan<byte> Value => _value;

    public SecretBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length > MaximumBytes) throw new ArgumentException("Secret exceeds its value bound.", nameof(value));
        _value = value.ToArray();
    }

    public byte[] Reveal(int minimumBytes, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumBytes);
        if (maximumBytes < minimumBytes || maximumBytes > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (_value.Length < minimumBytes || _value.Length > maximumBytes)
            throw new InvalidDataException("Protected material exceeds its configured encoding bounds.");
        return (byte[])_value.Clone();
    }

    public bool Equals(SecretBytes? other) => other is not null && CryptographicOperations.FixedTimeEquals(_value, other._value);
    public override bool Equals(object? obj) => obj is SecretBytes other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_value);
        return hash.ToHashCode();
    }
    public override string ToString() => "[redacted]";
}
