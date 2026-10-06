using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.UnitTests;

internal sealed class MemoryRegistryRepository : IRegistryRepository
{
    public RegistryState State { get; private set; } = RegistryState.Empty;
    public int Commits { get; private set; }
    public bool FailNext { get; set; }
    public ValueTask<RegistryState> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(State);
    }
    public ValueTask CommitAsync(long expectedRevision, RegistryState replacement, RegistryAudit audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(audit);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailNext) { FailNext = false; throw new IOException("Test storage failure."); }
        if (State.Revision != expectedRevision) throw new InvalidOperationException("Test revision conflict.");
        State = replacement;
        Commits++;
        return ValueTask.CompletedTask;
    }
}
