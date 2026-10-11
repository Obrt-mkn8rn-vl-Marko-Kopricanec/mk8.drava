using System.Text.Json;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.NoConf;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed partial class NoConfReconciler
{
    public async ValueTask<NoConfPolicyView> ReadPolicyViewAsync(bool includeHistory, CancellationToken cancellationToken)
    {
        await _compilationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireAuthority();
            await LoadPolicyAsync(cancellationToken).ConfigureAwait(false);
            var history = includeHistory ? await ReadHistoryAsync(cancellationToken).ConfigureAwait(false) : [];
            return new NoConfPolicyView(_acceptedPolicy, _appliedPolicyRevision, _store.Snapshot.Version, _registry.State.Revision, Failure, Compiled, history);
        }
        finally { _compilationGate.Release(); }
    }

    public async ValueTask<bool> UpdatePolicyAsync(long expectedRevision, string? json, bool importFile, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (importFile == (json is not null)) throw new InvalidDataException("Select one policy source.");
        await _compilationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireAuthority();
            EnsureBaseline();
            await LoadPolicyAsync(cancellationToken).ConfigureAwait(false);
            if ((_acceptedPolicy?.Revision ?? 0) != expectedRevision) return false;
            var policy = importFile ? await NoConfPolicyFile.ReadAsync(_policyPath, cancellationToken).ConfigureAwait(false) : NoConfPolicyFile.Parse(json ?? throw new InvalidDataException("Policy is missing."));
            return await AcceptPolicyAsync(policy, importFile ? "file" : "control", cancellationToken).ConfigureAwait(false);
        }
        finally { _compilationGate.Release(); }
    }

    public async ValueTask<bool> RollbackPolicyAsync(long expectedRevision, long targetRevision, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetRevision, 1);
        await _compilationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireAuthority();
            EnsureBaseline();
            await LoadPolicyAsync(cancellationToken).ConfigureAwait(false);
            if ((_acceptedPolicy?.Revision ?? 0) != expectedRevision) return false;
            var history = await ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var revision in history)
                if (revision.Revision == targetRevision) return await AcceptPolicyAsync(NoConfPolicyFile.Parse(revision.CanonicalJson), "control", cancellationToken).ConfigureAwait(false);
            throw new InvalidDataException("Rollback target is not in retained policy history.");
        }
        finally { _compilationGate.Release(); }
    }

    private async ValueTask<bool> AcceptPolicyAsync(NoConfPolicy policy, string source, CancellationToken cancellationToken)
    {
        var state = _registry.State;
        var candidate = _compiler.Compile(state, _baseline ?? throw new InvalidOperationException("Manual baseline is missing."), policy, _domain, _localNodeId);
        var desired = new PolicyRevision(checked((_acceptedPolicy?.Revision ?? 0) + 1), NoConfPolicyFile.Encode(policy), _clock.GetUtcNow(), "administrator", source);
        if (!await CommitPolicyAsync(desired, state.Revision, cancellationToken).ConfigureAwait(false)) return false;
        if (InstallPolicy(candidate, state, desired)) Volatile.Write(ref _failure, "");
        return true;
    }

    private async ValueTask LoadPolicyAsync(CancellationToken cancellationToken)
    {
        if (_policyLoaded) return;
        try
        {
            _acceptedPolicy = await _policies.ReadPolicyAsync(cancellationToken).ConfigureAwait(false);
            if (_acceptedPolicy is { } accepted) _ = NoConfPolicyFile.Parse(accepted.CanonicalJson);
            _policyLoaded = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _registry.FailStorage();
            throw;
        }
    }

    private async ValueTask<IReadOnlyList<PolicyRevision>> ReadHistoryAsync(CancellationToken cancellationToken)
    {
        try { return await _policies.ReadPolicyHistoryAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException) { _registry.FailStorage(); throw; }
    }

    private async ValueTask<bool> CommitPolicyAsync(PolicyRevision revision, long registryRevision, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _policies.TryCommitPolicyAsync(_acceptedPolicy?.Revision ?? 0, registryRevision, revision, cancellationToken).ConfigureAwait(false)) return false;
            RequireAuthority();
            _acceptedPolicy = revision;
            return true;
        }
        catch { _registry.FailStorage(); throw; }
    }

    private async ValueTask<(NoConfPolicy Policy, bool FileRejected)> ReadDesiredPolicyAsync(CancellationToken cancellationToken)
    {
        if (_acceptedPolicy is { Source: "control" } controlled) return (NoConfPolicyFile.Parse(controlled.CanonicalJson), false);
        try { return (await NoConfPolicyFile.ReadAsync(_policyPath, cancellationToken).ConfigureAwait(false), false); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or JsonException or IOException)
        {
            if (_acceptedPolicy is not { } accepted) throw;
            if (Failure.Length == 0) InvalidPolicy(_logger, exception);
            return (NoConfPolicyFile.Parse(accepted.CanonicalJson), true);
        }
    }

    private bool InstallPolicy(CompiledNoConfSnapshot candidate, RegistryState state, PolicyRevision policy)
    {
        lock (_publicationGate)
        {
            RequireAuthority();
            if (_registry.State.Revision != state.Revision)
            {
                Volatile.Write(ref _failure, "Accepted policy awaits compilation against current registry intent.");
                return false;
            }
            var snapshot = candidate.Snapshot.WithVersion(checked(_version + 1));
            var applied = new CompiledNoConfSnapshot(candidate.DesiredRevision, snapshot, candidate.Services);
            InvalidateChangedProofs(applied, state, retainUnchangedPublication: string.Equals(_policyHash, policy.Digest, StringComparison.Ordinal));
            _store.Replace(snapshot);
            _version = snapshot.Version;
            _policyHash = policy.Digest;
            _appliedPolicyRevision = policy.Revision;
            Volatile.Write(ref _compiled, applied);
            return true;
        }
    }

    private CompiledNoConfSnapshot CompileDesiredPolicy(RegistryState state, NoConfPolicy policy, ref bool fileRejected)
    {
        var baseline = _baseline ?? throw new InvalidOperationException("Manual baseline is missing.");
        try { return _compiler.Compile(state, baseline, policy, _domain, _localNodeId); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            if (_acceptedPolicy is not { Source: "file" } accepted) throw;
            if (Failure.Length == 0) InvalidPolicy(_logger, exception);
            fileRejected = true;
            return _compiler.Compile(state, baseline, NoConfPolicyFile.Parse(accepted.CanonicalJson), _domain, _localNodeId);
        }
    }

    private void SetPolicyFailure(bool fileRejected)
        => Volatile.Write(ref _failure, fileRejected ? "Policy file rejected; the durable accepted revision is retained." : "");

    private void RequireAuthority()
    {
        if (!_registry.StorageHealthy) throw new InvalidOperationException("Policy authority requires healthy durable registry state.");
    }
}
