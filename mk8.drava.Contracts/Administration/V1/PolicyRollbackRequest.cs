namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class PolicyRollbackRequest
{
    public long ExpectedRevision { get; init; }
    public long TargetRevision { get; init; }
}
