using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Transport.Registration;

namespace Mk8.Drava.Transport.Certificates;

internal static class EnrollmentCertificateScope
{
    public static void Require(X509Certificate2 certificate, IReadOnlyList<string> hostNames, string address)
    {
        using var key = certificate.GetECDsaPublicKey();
        if (key is null || !RegistrationProof.IsP256(key))
            throw new InvalidDataException("Private listener certificate requires a P256 key.");
        var extension = certificate.Extensions["2.5.29.17"] ?? throw new InvalidDataException("Private listener certificate requires SAN identities.");
        var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
        var names = reader.ReadSequence();
        var remaining = new HashSet<string>(hostNames, StringComparer.Ordinal);
        var expectedAddress = IPAddress.Parse(address);
        var addressSeen = false;
        while (names.HasData)
        {
            var tag = names.PeekTag();
            if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
            {
                var name = names.ReadCharacterString(UniversalTagNumber.IA5String, tag);
                if (!remaining.Remove(name)) throw new InvalidDataException("Private listener certificate has an extra or repeated DNS identity.");
            }
            else if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 7)))
            {
                var bytes = names.ReadOctetString(tag);
                if (bytes.Length is not (4 or 16) || addressSeen || !new IPAddress(bytes).Equals(expectedAddress))
                    throw new InvalidDataException("Private listener certificate has an extra or invalid IP identity.");
                addressSeen = true;
            }
            else throw new InvalidDataException("Private listener certificate has an unapproved SAN identity type.");
        }
        reader.ThrowIfNotEmpty();
        if (remaining.Count != 0 || !addressSeen) throw new InvalidDataException("Private listener certificate lacks an approved identity.");
    }
}
