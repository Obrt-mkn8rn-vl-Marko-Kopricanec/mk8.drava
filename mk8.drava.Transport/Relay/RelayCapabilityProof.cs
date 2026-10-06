using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Transport.Relay;

public static class RelayCapabilityProof
{
    public const int MaximumPayloadBytes = 16 * 1024;

    public static byte[] Digest(string siteId, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        if (siteId.Length > 128 || payload.Length is < 1 or > MaximumPayloadBytes) throw new InvalidDataException("Relay capability exceeds its proof bounds.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData("mk8.drava/relay-capability/v1\0"u8);
        var site = Encoding.UTF8.GetBytes(siteId);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, site.Length);
        digest.AppendData(length);
        digest.AppendData(site);
        digest.AppendData(SHA256.HashData(payload));
        return digest.GetHashAndReset();
    }

    public static byte[] Sign(ECDsa privateKey, string siteId, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (!RegistrationProof.IsP256(privateKey)) throw new InvalidDataException("Relay controller requires a P-256 key.");
        return privateKey.SignHash(Digest(siteId, payload), DSASignatureFormat.Rfc3279DerSequence);
    }

    public static bool Verify(ECDsa publicKey, string siteId, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return RegistrationProof.IsP256(publicKey) && signature.Length is >= 64 and <= 80 &&
            publicKey.VerifyHash(Digest(siteId, payload), signature, DSASignatureFormat.Rfc3279DerSequence);
    }
}
