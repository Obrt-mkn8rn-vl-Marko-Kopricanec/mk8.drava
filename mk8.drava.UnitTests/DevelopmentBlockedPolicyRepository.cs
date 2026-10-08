using Mk8.Drava.Application.BLL.NoConf;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentBlockedPolicyRepository(IPolicyRepository inner) : IPolicyRepository, IDisposable
{
    private readonly SemaphoreSlim _release = new(0, 1);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Release() { if (_release.CurrentCount == 0) _release.Release(); }

    public async ValueTask<PolicyRevision?> ReadPolicyAsync(CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await inner.ReadPolicyAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<PolicyRevision>> ReadPolicyHistoryAsync(CancellationToken cancellationToken) => inner.ReadPolicyHistoryAsync(cancellationToken);
    public ValueTask<bool> TryCommitPolicyAsync(long expectedPolicyRevision, long expectedRegistryRevision, PolicyRevision replacement, CancellationToken cancellationToken) =>
        inner.TryCommitPolicyAsync(expectedPolicyRevision, expectedRegistryRevision, replacement, cancellationToken);
    public void Dispose() => _release.Dispose();
}
