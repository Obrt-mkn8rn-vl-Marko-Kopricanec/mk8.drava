namespace Mk8.Drava.Configuration;

// This remains part of the ordinary collection quota, alongside bootstrap and other credentials.
public sealed class PortableManualConfigurationDocument : IEquatable<PortableManualConfigurationDocument>
{
    private readonly string _document;
    public static PortableManualConfigurationDocument Empty { get; } = new("");
    public bool IsEmpty => _document.Length == 0;

    public PortableManualConfigurationDocument(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        InstallationCollectionBounds.RequireValue(document, 4 * 1024 * 1024);
        _document = document;
    }

    public string Reveal() => _document;
    public bool Equals(PortableManualConfigurationDocument? other) => other is not null && string.Equals(_document, other._document, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is PortableManualConfigurationDocument other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_document);
    public override string ToString() => "[redacted]";
}
