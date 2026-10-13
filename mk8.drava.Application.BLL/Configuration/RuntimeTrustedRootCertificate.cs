using System.Security.Cryptography;
using System.Text;

namespace Mk8.Drava.Application.BLL.Configuration;

public sealed record RuntimeTrustedRootCertificate
{
    private readonly byte[] _certificate;

    public RuntimeTrustedRootCertificate(string materialId, string sha256, ReadOnlySpan<byte> certificate)
    {
        ValidateReference(materialId, sha256);
        if (certificate.Length is < 1 or > 65536)
            throw new ArgumentException("Trusted root certificate material exceeds its bounds.", nameof(certificate));
        MaterialId = materialId;
        Sha256 = sha256;
        _certificate = certificate.ToArray();
        var materialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(materialId)));
        Identity = $"root={sha256}|root-material-sha256={materialHash}";
    }

    public static void ValidateReference(string materialId, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (materialId.Length > 128 || !string.Equals(materialId, materialId.Trim(), StringComparison.Ordinal)
            || materialId.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.' or '-')))
        {
            throw new ArgumentException("Trusted root material ID must use 1 to 128 ASCII letters, digits, underscores, periods or hyphens.", nameof(materialId));
        }
        if (sha256.Length != 64 || sha256.Any(static character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException("Trusted root certificate SHA256 must use 64 canonical uppercase hexadecimal characters.", nameof(sha256));
        }
    }

    public string MaterialId { get; }
    public string Sha256 { get; }
    public string Identity { get; }

    public byte[] CopyCertificate() => (byte[])_certificate.Clone();

    public bool Equals(RuntimeTrustedRootCertificate? other) => other is not null
        && string.Equals(MaterialId, other.MaterialId, StringComparison.Ordinal)
        && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal)
        && _certificate.AsSpan().SequenceEqual(other._certificate);

    public override int GetHashCode() => HashCode.Combine(MaterialId, Sha256);
}
