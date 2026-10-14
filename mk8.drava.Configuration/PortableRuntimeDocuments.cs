namespace Mk8.Drava.Configuration;

public sealed class PortableRuntimeDocuments
{
    public PortableRuntimeDocuments(PortablePolicyArchive policyArchive, PortableManualConfigurationDocument manualConfiguration)
    {
        ArgumentNullException.ThrowIfNull(policyArchive);
        ArgumentNullException.ThrowIfNull(manualConfiguration);
        PolicyArchive = policyArchive;
        ManualConfiguration = manualConfiguration;
    }
    public PortablePolicyArchive PolicyArchive { get; }
    public PortableManualConfigurationDocument ManualConfiguration { get; }
    public override string ToString() => "[redacted]";
}
