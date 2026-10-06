namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record ProxyConfigurationDiscoveryResponse
{
    public ProxyConfigurationDiscoveryResponse(ProxyFilesystemLayoutResponse layout, IReadOnlyList<ProxyConfigurationFileDiscoveryResponse> files, IReadOnlyList<string> createdPaths, IReadOnlyList<string> existingPaths)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
        Files = ApiResponseList.Copy(files);
        CreatedPaths = ApiResponseList.Copy(createdPaths);
        ExistingPaths = ApiResponseList.Copy(existingPaths);
    }

    public ProxyFilesystemLayoutResponse Layout { get; }
    public IReadOnlyList<ProxyConfigurationFileDiscoveryResponse> Files { get; }
    public IReadOnlyList<string> CreatedPaths { get; }
    public IReadOnlyList<string> ExistingPaths { get; }
}
