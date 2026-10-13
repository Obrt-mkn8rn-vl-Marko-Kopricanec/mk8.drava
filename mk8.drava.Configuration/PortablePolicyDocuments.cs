namespace Mk8.Drava.Configuration;

public sealed class PortablePolicyDocuments
{
    internal PortablePolicyDocuments(string? source, string? archive)
    {
        InstallationCollectionBounds.RequireValue(source ?? "", InstallationCollectionBounds.MaximumPolicySourceBytes);
        ConfiguredSource = new SecretText(source ?? "");
        Archive = archive is null ? PortablePolicyArchive.Empty : new PortablePolicyArchive(archive);
    }

    public SecretText ConfiguredSource { get; }
    public PortablePolicyArchive Archive { get; }
}
