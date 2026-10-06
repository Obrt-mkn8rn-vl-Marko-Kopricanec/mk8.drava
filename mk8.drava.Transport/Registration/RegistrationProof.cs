using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mk8.Drava.Transport.Registration;

public static class RegistrationProof
{
    public const int MaximumPayloadBytes = 16 * 1024;
    public const int MaximumCertificateBytes = 16 * 1024;

    // Domain separation prevents a node readiness signature or another site's signature from authorizing membership.
    public static byte[] Digest(string siteId, uint version, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        if (siteId.Length > 128 || version != 1 || nonce.Length != 32 || payload.Length is < 1 or > MaximumPayloadBytes)
            throw new InvalidDataException("Invalid registration proof bounds or version.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData("mk8.drava/registration/v1\0"u8);
        var encodedSite = Encoding.UTF8.GetBytes(siteId);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, encodedSite.Length);
        digest.AppendData(length);
        digest.AppendData(encodedSite);
        digest.AppendData(nonce);
        digest.AppendData(SHA256.HashData(payload));
        return digest.GetHashAndReset();
    }

    public static byte[] Sign(ECDsa privateKey, string siteId, uint version, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (!IsP256(privateKey)) throw new InvalidDataException("Enrollment requires a P-256 key.");
        return privateKey.SignHash(Digest(siteId, version, nonce, payload), DSASignatureFormat.Rfc3279DerSequence);
    }

    public static bool Verify(ECDsa publicKey, string siteId, uint version, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return IsP256(publicKey) && signature.Length is >= 64 and <= 80 &&
            publicKey.VerifyHash(Digest(siteId, version, nonce, payload), signature, DSASignatureFormat.Rfc3279DerSequence);
    }

    public static bool IsP256(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.KeySize == 256 && string.Equals(key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value, "1.2.840.10045.3.1.7", StringComparison.Ordinal);
    }
}
