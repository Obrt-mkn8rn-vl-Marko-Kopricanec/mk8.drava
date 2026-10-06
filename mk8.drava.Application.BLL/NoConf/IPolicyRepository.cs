namespace Mk8.Drava.Application.BLL.NoConf;

public interface IPolicyRepository
{
    ValueTask<PolicyRevision?> ReadPolicyAsync(CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<PolicyRevision>> ReadPolicyHistoryAsync(CancellationToken cancellationToken);
    ValueTask<bool> TryCommitPolicyAsync(long expectedPolicyRevision, long expectedRegistryRevision,
        PolicyRevision replacement, CancellationToken cancellationToken);
}
