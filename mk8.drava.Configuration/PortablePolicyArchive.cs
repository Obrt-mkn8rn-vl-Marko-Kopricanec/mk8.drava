using System.Text;

namespace Mk8.Drava.Configuration;

// This purpose-specific value can hold retained policies without enlarging ordinary secret values.
public sealed class PortablePolicyArchive : IEquatable<PortablePolicyArchive>
{
    private readonly string _document;
    public static PortablePolicyArchive Empty { get; } = new("");

    public PortablePolicyArchive(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        InstallationCollectionBounds.RequireValue(document, InstallationCollectionBounds.MaximumPolicyArchiveBytes);
        _document = document;
    }

    public bool IsEmpty => _document.Length == 0;
    public string Reveal() => _document;
    public bool Equals(PortablePolicyArchive? other) => other is not null && string.Equals(_document, other._document, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is PortablePolicyArchive other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_document);
    public override string ToString() => "[redacted]";
}
