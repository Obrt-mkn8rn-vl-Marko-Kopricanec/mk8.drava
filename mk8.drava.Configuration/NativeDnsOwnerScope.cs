namespace Mk8.Drava.Configuration;

public sealed record NativeDnsOwnerScope(string Owner, ushort Type)
{
    internal void Validate(string origin, bool acme)
    {
        if (Owner is null || Owner.Length > 253 || (acme ? Type != 16 || !Owner.StartsWith("_acme-challenge.", StringComparison.Ordinal) : Type is not (1 or 28)))
            throw new InvalidDataException("Native DNS owner/type exceeds its records or separate ACME profile.");
        var name = acme ? Owner[16..] : Owner;
        NativeDnsManagementSettings.RequireDomain(name);
        if (!string.Equals(name, origin, StringComparison.Ordinal) && !name.EndsWith("." + origin, StringComparison.Ordinal)) throw new InvalidDataException("Native DNS scope is outside its origin.");
    }
}
