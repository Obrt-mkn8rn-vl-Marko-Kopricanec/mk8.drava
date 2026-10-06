namespace Mk8.Drava.Application.BLL.Registry;

public sealed class RegistryCoordinator : IDisposable
{
    private readonly IRegistryRepository _repository;
    private readonly DestinationAvailabilityStore _availability;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private RegistryState _state = RegistryState.Empty;
    private bool _initialized;
    private bool _storageHealthy = true;

    public RegistryCoordinator(IRegistryRepository repository, DestinationAvailabilityStore availability, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _availability = availability;
        _clock = clock;
    }

    public RegistryState State => Volatile.Read(ref _state);

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) throw new InvalidOperationException("Registry is already initialized.");
            var state = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _state, state);
            _initialized = true;
        }
        finally { _writer.Release(); }
    }

    public async ValueTask EnrollAsync(NodeGrant grant, string administrator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        RegistryNames.RequireLabel(administrator);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireInitialized();
            if (State.Grants.TryGetValue(grant.NodeId, out var previous) && !string.Equals(previous.OwnerId, grant.OwnerId, StringComparison.Ordinal))
                throw new InvalidDataException("An enrolled node cannot change owner implicitly.");
            _availability.RevokeNode(grant.NodeId);
            var grants = new Dictionary<string, NodeGrant>(State.Grants, StringComparer.Ordinal) { [grant.NodeId] = grant };
            await CommitAsync(new RegistryState(checked(State.Revision + 1), grants, State.Instances, State.Tombstones), "enroll", administrator, grant.NodeId, cancellationToken).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
    }

    public async ValueTask<InstanceIntent> RegisterAsync(string authenticatedFingerprint, InstanceIntent intent, TimeSpan lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        DestinationAvailabilityStore.ValidateLease(lease);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireInitialized();
            var grant = Authorize(authenticatedFingerprint, intent);
            if (intent.Draining) throw new InvalidDataException("Register cannot undo or initiate draining.");
            foreach (var tombstone in State.Tombstones)
                if (string.Equals(tombstone, intent.Identity.Partition, StringComparison.Ordinal)) throw new InvalidDataException("This instance boot is revoked.");
            EnsureServiceOwnership(intent);
            var tombstones = new List<string>(State.Tombstones);
            if (State.Instances.TryGetValue(intent.Identity.InstanceId, out var previous))
            {
                EnsureStableInstance(previous, intent);
                if (previous.Identity == intent.Identity && previous.Draining) throw new InvalidDataException("A draining boot cannot register again.");
                if (previous.Identity != intent.Identity) tombstones.Add(previous.Identity.Partition);
                if (previous == intent)
                {
                    _availability.Renew(intent, grant.NotAfterUtc, lease);
                    return previous;
                }
                _availability.Revoke(previous.Identity);
            }
            var instances = new Dictionary<string, InstanceIntent>(State.Instances, StringComparer.Ordinal) { [intent.Identity.InstanceId] = intent };
            await CommitAsync(new RegistryState(checked(State.Revision + 1), State.Grants, instances, tombstones), "register", grant.OwnerId, intent.Identity.Partition, cancellationToken).ConfigureAwait(false);
            _availability.Renew(intent, grant.NotAfterUtc, lease);
            return intent;
        }
        finally { _writer.Release(); }
    }

    public async ValueTask RenewAsync(string authenticatedFingerprint, RegisteredUpstreamIdentity identity, TimeSpan lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        DestinationAvailabilityStore.ValidateLease(lease);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireInitialized();
            var intent = FindCurrent(identity);
            var grant = Authorize(authenticatedFingerprint, intent);
            if (intent.Draining) throw new InvalidDataException("This instance is draining.");
            _availability.Renew(intent, grant.NotAfterUtc, lease);
        }
        finally { _writer.Release(); }
    }

    public async ValueTask DrainAsync(string authenticatedFingerprint, RegisteredUpstreamIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireInitialized();
            var intent = FindCurrent(identity);
            var grant = Authorize(authenticatedFingerprint, intent);
            _availability.Revoke(identity);
            if (intent.Draining) return;
            var instances = new Dictionary<string, InstanceIntent>(State.Instances, StringComparer.Ordinal) { [identity.InstanceId] = intent.Drain() };
            await CommitAsync(new RegistryState(checked(State.Revision + 1), State.Grants, instances, State.Tombstones), "drain", grant.OwnerId, identity.Partition, cancellationToken).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
    }

    public async ValueTask RevokeNodeAsync(string nodeId, string administrator, CancellationToken cancellationToken)
    {
        RegistryNames.RequireLabel(nodeId);
        RegistryNames.RequireLabel(administrator);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireInitialized();
            if (!State.Grants.TryGetValue(nodeId, out var grant)) throw new InvalidDataException("Unknown enrollment.");
            _availability.RevokeNode(nodeId);
            var grants = new Dictionary<string, NodeGrant>(State.Grants, StringComparer.Ordinal) { [nodeId] = grant.Revoke() };
            await CommitAsync(new RegistryState(checked(State.Revision + 1), grants, State.Instances, State.Tombstones), "revoke-node", administrator, nodeId, cancellationToken).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
    }

    public void Dispose() => _writer.Dispose();

    private InstanceIntent FindCurrent(RegisteredUpstreamIdentity identity)
    {
        if (!State.Instances.TryGetValue(identity.InstanceId, out var intent) || intent.Identity != identity)
            throw new InvalidDataException("Unknown or superseded instance boot.");
        return intent;
    }

    private NodeGrant Authorize(string fingerprint, InstanceIntent intent)
    {
        RegistryNames.RequireFingerprint(fingerprint);
        if (!State.Grants.TryGetValue(intent.Identity.NodeId, out var grant) || !string.Equals(fingerprint, grant.CertificateFingerprint, StringComparison.Ordinal) ||
            !grant.Authorizes(intent, _clock.GetUtcNow())) throw new UnauthorizedAccessException("Enrollment does not authorize this endpoint and service.");
        return grant;
    }

    private void EnsureServiceOwnership(InstanceIntent intent)
    {
        foreach (var previous in State.Instances.Values)
            if (string.Equals(previous.Identity.ServiceId, intent.Identity.ServiceId, StringComparison.Ordinal) &&
                (!string.Equals(previous.Identity.OwnerId, intent.Identity.OwnerId, StringComparison.Ordinal) ||
                 !string.Equals(previous.Identity.ContractId, intent.Identity.ContractId, StringComparison.Ordinal)))
                throw new InvalidDataException("Service owner or contract conflicts with an existing pool.");
    }

    private static void EnsureStableInstance(InstanceIntent previous, InstanceIntent replacement)
    {
        if (!string.Equals(previous.Identity.NodeId, replacement.Identity.NodeId, StringComparison.Ordinal) || !string.Equals(previous.Identity.OwnerId, replacement.Identity.OwnerId, StringComparison.Ordinal) ||
            !string.Equals(previous.Identity.ServiceId, replacement.Identity.ServiceId, StringComparison.Ordinal) || !string.Equals(previous.Identity.ContractId, replacement.Identity.ContractId, StringComparison.Ordinal))
            throw new InvalidDataException("An instance identity cannot move between owners, nodes or contracts.");
    }

    private async ValueTask CommitAsync(RegistryState replacement, string operation, string actor, string subject, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.CommitAsync(State.Revision, replacement, new RegistryAudit(_clock.GetUtcNow(), operation, actor, subject), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _storageHealthy = false;
            foreach (var node in State.Grants.Keys) _availability.RevokeNode(node);
            throw;
        }
        Volatile.Write(ref _state, replacement);
    }

    private void RequireInitialized()
    {
        if (!_initialized || !_storageHealthy) throw new InvalidOperationException("Registry requires healthy initialized durable state.");
    }
}
