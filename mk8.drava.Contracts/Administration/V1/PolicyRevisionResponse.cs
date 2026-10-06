namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class PolicyRevisionResponse
{
    public long Revision { get; init; }
    public string Digest { get; init; } = "";
    public DateTimeOffset AcceptedAtUtc { get; init; }
    public string Actor { get; init; } = "";
    public string Source { get; init; } = "";
}
