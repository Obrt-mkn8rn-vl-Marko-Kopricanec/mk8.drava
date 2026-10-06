namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class RegistryStateResponse
{
    public long Revision { get; init; }
    public bool StorageHealthy { get; init; }
    public int TombstoneCount { get; init; }
    public string NextId { get; init; } = "";
    public IReadOnlyList<NodeGrantResponse> Nodes { get; init; } = [];
    public IReadOnlyList<RegisteredInstanceResponse> Instances { get; init; } = [];
}
