using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.NoConf;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentReconcilerControl(CancellationToken guard) : IPolicyRepository, INoConfPolicySource,
    IRegisteredReadinessProbe, IServiceDnsVerifier, IGatewayPublicationSource
{
    private int _historyReads;
    internal bool HoldPolicyRead { get; init; }
    internal bool HoldSourceRead { get; init; }
    internal bool HoldProbe { get; init; }
    internal Exception? Failure { get; init; }
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int HistoryReads => Volatile.Read(ref _historyReads);

    public async ValueTask<PolicyRevision?> ReadPolicyAsync(CancellationToken cancellationToken)
    {
        if (HoldPolicyRead) await HoldAsync(cancellationToken).ConfigureAwait(false);
        return new PolicyRevision(1, NoConfPolicyFile.Encode(new NoConfPolicy()), DateTimeOffset.UnixEpoch, "operator", "file");
    }

    public ValueTask<IReadOnlyList<PolicyRevision>> ReadPolicyHistoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _historyReads);
        return ValueTask.FromResult<IReadOnlyList<PolicyRevision>>([]);
    }

    public ValueTask<bool> TryCommitPolicyAsync(long expectedPolicyRevision, long expectedRegistryRevision, PolicyRevision replacement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("The controlled reconciler already has the requested durable policy.");
    }

    public async ValueTask<NoConfPolicy?> ReadAsync(CancellationToken cancellationToken)
    {
        if (HoldSourceRead) await HoldAsync(cancellationToken).ConfigureAwait(false);
        return new NoConfPolicy();
    }

    public async ValueTask<bool> CheckAsync(InstanceIntent intent, RuntimeUpstream upstream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(upstream);
        if (HoldProbe) await HoldAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    public ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(DnsPublicationProof.Missing);
    }

    public GatewayPublicationProof? ReadPublicationProof() => null;

    private async Task HoldAsync(CancellationToken cancellationToken)
    {
        var canceled = cancellationToken.Register(() => Canceled.TrySetResult());
        await using var canceledLifetime = canceled.ConfigureAwait(false);
        Entered.TrySetResult();
        await Release.Task.WaitAsync(guard).ConfigureAwait(false);
        if (Failure is not null) throw Failure;
    }
}
