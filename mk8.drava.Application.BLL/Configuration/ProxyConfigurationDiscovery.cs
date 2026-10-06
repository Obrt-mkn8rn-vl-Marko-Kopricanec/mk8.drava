namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record ProxyConfigurationDiscovery
{
    public ProxyConfigurationDiscovery(ProxyFilesystemLayout layout, IReadOnlyList<ProxyConfigurationFileDiscovery> files, IReadOnlyList<string> createdPaths, IReadOnlyList<string> existingPaths)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
        Files = RuntimeList.Copy(files);
        CreatedPaths = RuntimeList.Copy(createdPaths);
        ExistingPaths = RuntimeList.Copy(existingPaths);
    }

    public ProxyFilesystemLayout Layout { get; }
    public IReadOnlyList<ProxyConfigurationFileDiscovery> Files { get; }
    public IReadOnlyList<string> CreatedPaths { get; }
    public IReadOnlyList<string> ExistingPaths { get; }

    public ProxyConfigurationDiscovery WithFiles(IReadOnlyList<ProxyConfigurationFileDiscovery> files)
    {
        return new ProxyConfigurationDiscovery(Layout, files, CreatedPaths, ExistingPaths);
    }
}
