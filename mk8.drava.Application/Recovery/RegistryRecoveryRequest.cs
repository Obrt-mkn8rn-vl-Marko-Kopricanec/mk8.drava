namespace Mk8.Drava.Application.Recovery;

internal sealed record RegistryRecoveryRequest
{
    public int SchemaVersion { get; init; }
    public string SourceDirectory { get; init; } = "";
    public string DestinationDirectory { get; init; } = "";
    public string SiteId { get; init; } = "";
    public long MaximumBytes { get; init; }
    public string ExpectedImageSha256 { get; init; } = "";
}
