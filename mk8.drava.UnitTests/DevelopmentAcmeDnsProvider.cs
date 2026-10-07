using Mk8.Drava.Application.INF.Acme;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentAcmeDnsProvider : IAcmeDns01ChallengeProvider
{
    public List<AcmeDns01Record> Records { get; } = [];
    public int Published { get; private set; }
    public int Removed { get; private set; }
    public int Proofs { get; private set; }
    public bool MissingProof { get; init; }
    public bool BlockProof { get; init; }
    public bool FailCleanup { get; init; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<AcmeDns01Record> PublishAsync(string host, string value, string operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AcmeDns01Record record = new DevelopmentRecord(host, value, operationId);
        Records.Add(record); Published++;
        return ValueTask.FromResult(record);
    }

    public async ValueTask<bool> IsPropagatedAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        Proofs++; Entered.TrySetResult();
        try
        {
            if (BlockProof) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return !MissingProof && Records.Contains(record);
        }
        finally { Exited.TrySetResult(); }
    }

    public ValueTask RemoveAsync(AcmeDns01Record record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailCleanup) throw new InvalidDataException("Owned development cleanup rejected.");
        if (!Records.Remove(record)) throw new InvalidDataException("Development challenge identity is absent.");
        Removed++;
        return ValueTask.CompletedTask;
    }

    private sealed record DevelopmentRecord(string OwnerName, string Digest, string OperationId) : AcmeDns01Record(OwnerName, Digest);
}
