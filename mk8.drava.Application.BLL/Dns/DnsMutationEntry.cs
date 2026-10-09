using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.Dns;

public sealed record DnsMutationEntry(Guid OperationId, string Owner, ushort Type, uint Ttl, string Value, bool Remove,
    Guid RelatedOperationId, long ExpectedZoneRevision, string State, long Revision, uint Serial, string ContentHash, string OperationLocation)
{
    public void Validate(string purpose)
    {
        if (OperationId == Guid.Empty || ExpectedZoneRevision <= 0 || Owner is null || Owner.Length is < 3 or > 253 ||
            State is not ("prepared" or "accepted" or "activated" or "rejected" or "conflict" or "completed") || Value is null || ContentHash is null || OperationLocation is null || OperationLocation.Length is < 1 or > 512)
            throw new InvalidDataException("Invalid DNS operation identity or bounds.");
        var name = Type == 16 && Owner.StartsWith("_acme-challenge.", StringComparison.Ordinal) ? Owner[16..] : Owner;
        foreach (var label in name.Split('.')) RegistryNames.RequireLabel(label);
        if (!name.Contains('.', StringComparison.Ordinal) || (string.Equals(purpose, "records", StringComparison.Ordinal) ? Type is not (1 or 28) || Remove : Type != 16 || string.Equals(name, Owner, StringComparison.Ordinal)))
            throw new InvalidDataException("DNS operation exceeds its journal purpose.");
        var data = Convert.FromBase64String(Value);
        if (!string.Equals(Convert.ToBase64String(data), Value, StringComparison.Ordinal) ||
            data.Length != (Type == 1 ? 4 : Type == 28 ? 16 : 44) || Ttl is < 60 or > 86400 || Type == 16 && (Ttl > 3600 || data[0] != 43))
            throw new InvalidDataException("DNS operation has invalid canonical RDATA or TTL.");
        if (Type == 16)
        {
            var digest = System.Text.Encoding.ASCII.GetString(data, 1, 43);
            if (digest.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
                throw new InvalidDataException("DNS01 RDATA must contain one canonical SHA256 base64url digest.");
            var decoded = Convert.FromBase64String(digest.Replace('-', '+').Replace('_', '/') + "=");
            if (decoded.Length != 32 || !string.Equals(Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_'), digest, StringComparison.Ordinal))
                throw new InvalidDataException("DNS01 RDATA has noncanonical digest padding bits.");
        }
        if (Remove ? RelatedOperationId == Guid.Empty : RelatedOperationId != Guid.Empty)
            throw new InvalidDataException("DNS removal requires a related owned addition.");
        if (State is "prepared" or "rejected" or "conflict")
        {
            if (Revision != 0 || Serial != 0 || ContentHash.Length != 0) throw new InvalidDataException("Unacknowledged DNS operation cannot contain a receipt.");
        }
        else
        {
            if (Revision != checked(ExpectedZoneRevision + 1)) throw new InvalidDataException("DNS receipt must advance the expected zone revision.");
            if (ContentHash.Length != 64 || ContentHash.Any(static character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("DNS receipt requires its exact hexadecimal content digest.");
        }
    }

    public bool HasSameIntent(DnsMutationEntry other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return OperationId == other.OperationId && string.Equals(Owner, other.Owner, StringComparison.Ordinal) && Type == other.Type &&
            Ttl == other.Ttl && string.Equals(Value, other.Value, StringComparison.Ordinal) && Remove == other.Remove && RelatedOperationId == other.RelatedOperationId &&
            ExpectedZoneRevision == other.ExpectedZoneRevision && string.Equals(OperationLocation, other.OperationLocation, StringComparison.Ordinal);
    }
}
