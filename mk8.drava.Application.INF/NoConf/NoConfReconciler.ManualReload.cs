using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed partial class NoConfReconciler
{
    public async ValueTask<ProxyConfigurationSnapshot> ApplyManualBaselineAsync(ProxyConfigurationSnapshot baseline,
        Func<ProxyConfigurationSnapshot, ProxyConfigurationSnapshot> activate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(activate);
        await _compilationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureBaseline();
            RequireAuthority();
            await LoadPolicyAsync(cancellationToken).ConfigureAwait(false);
            var policy = _acceptedPolicy is { } accepted ? NoConfPolicyFile.Parse(accepted.CanonicalJson) : await NoConfPolicyFile.ReadAsync(_policyPath, cancellationToken).ConfigureAwait(false);
            var state = _registry.State;
            var candidate = _compiler.Compile(state, baseline, policy, _domain, _localNodeId);
            var snapshot = candidate.Snapshot.WithVersion(checked(_version + 1));
            var applied = new CompiledNoConfSnapshot(candidate.DesiredRevision, snapshot, candidate.Services);
            lock (_publicationGate)
            {
                if (_registry.State.Revision != state.Revision) throw new InvalidDataException("Registry changed during manual reload; retry the operation.");
                cancellationToken.ThrowIfCancellationRequested();
                InvalidateChangedProofs(applied, state);
                var installed = activate(snapshot);
                if (!ReferenceEquals(installed, snapshot)) throw new InvalidOperationException("Snapshot activation changed the compiled candidate.");
                _baseline = baseline;
                _version = snapshot.Version;
                Volatile.Write(ref _compiled, applied);
            }
            _policyHash = _acceptedPolicy?.Digest ?? "";
            _appliedPolicyRevision = _acceptedPolicy?.Revision ?? 0;
            Volatile.Write(ref _failure, "");
            return snapshot;
        }
        finally { _compilationGate.Release(); }
    }
}
