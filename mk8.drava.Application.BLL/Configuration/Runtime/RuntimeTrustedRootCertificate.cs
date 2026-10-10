using System.Security.Cryptography;
using System.Text;

namespace Mk8.Drava.Application.BLL.Configuration;

public sealed record RuntimeTrustedRootCertificate
{
    public RuntimeTrustedRootCertificate(string certificatePath, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (certificatePath.Length > 4096 || !Path.IsPathFullyQualified(certificatePath)
            || !string.Equals(certificatePath, certificatePath.Trim(), StringComparison.Ordinal)
            || certificatePath.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException("Trusted root certificate path must be a bounded absolute file path.", nameof(certificatePath));
        }
        if (sha256.Length != 64 || sha256.Any(static character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException("Trusted root certificate SHA256 must use 64 canonical uppercase hexadecimal characters.", nameof(sha256));
        }
        CertificatePath = certificatePath;
        Sha256 = sha256;
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(certificatePath)));
        Identity = $"root={sha256}|root-path-sha256={pathHash}";
    }

    public string CertificatePath { get; }
    public string Sha256 { get; }
    public string Identity { get; }
}
