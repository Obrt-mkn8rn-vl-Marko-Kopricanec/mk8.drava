using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.INF.Runtime;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed partial class NoConfReconciler : BackgroundService
{
    private readonly RegistryCoordinator _registry;
    private readonly IPolicyRepository _policies;
    private readonly DestinationAvailabilityStore _availability;
    private readonly ProxyConfigurationStore _store;
    private readonly NoConfSnapshotCompiler _compiler;
    private readonly IRegisteredReadinessProbe _readiness;
    private readonly IServiceDnsVerifier _dns;
    private readonly IGatewayPublicationSource _gateway;
    private readonly string _policyPath;
    private readonly string _domain;
    private readonly string _localNodeId;
    private readonly TimeProvider _clock;
    private readonly ILogger<NoConfReconciler> _logger;
    private readonly Lock _publicationGate = new();
    private readonly SemaphoreSlim _compilationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, ReadinessTracker> _trackers = new(StringComparer.Ordinal);
    private ProxyConfigurationSnapshot? _baseline;
    private CompiledNoConfSnapshot? _compiled;
    private string _policyHash = "";
    private int _version;
    private string _failure = "";
    private PolicyRevision? _acceptedPolicy;
    private bool _policyLoaded;
    private long _appliedPolicyRevision;

    public NoConfReconciler(RegistryCoordinator registry, DestinationAvailabilityStore availability, ProxyConfigurationStore store,
        NoConfSnapshotCompiler compiler, IRegisteredReadinessProbe readiness, IServiceDnsVerifier dns, IGatewayPublicationSource gateway,
        string policyPath, string domain, string localNodeId, TimeProvider clock, ILogger<NoConfReconciler> logger, IPolicyRepository policies)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(dns);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(policyPath)) throw new ArgumentException("Policy path must be absolute.", nameof(policyPath));
        ArgumentNullException.ThrowIfNull(domain);
        RegistryNames.RequireLabel(localNodeId);
        _registry = registry; _availability = availability; _store = store; _compiler = compiler; _readiness = readiness; _dns = dns; _gateway = gateway;
        _policies = policies;
        _policyPath = policyPath; _domain = domain; _localNodeId = localNodeId; _clock = clock; _logger = logger;
    }

    public CompiledNoConfSnapshot? Compiled => Volatile.Read(ref _compiled);
    public string Failure => Volatile.Read(ref _failure);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        await Task.WhenAll(RunLoopAsync(CompileLoopAsync, lifetime), RunLoopAsync(ProbeLoopAsync, lifetime)).ConfigureAwait(false);
    }

    private static async Task RunLoopAsync(Func<CancellationToken, Task> loop, CancellationTokenSource lifetime)
    {
        try { await loop(lifetime.Token).ConfigureAwait(false); }
        catch { await lifetime.CancelAsync().ConfigureAwait(false); throw; }
    }

    private async Task CompileLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _clock);
        do
        {
            await CompileLatestAsync(stoppingToken).ConfigureAwait(false);
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task ProbeLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), _clock);
        do { await ProbeAndPublishAsync(stoppingToken).ConfigureAwait(false); }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async ValueTask CompileLatestAsync(CancellationToken cancellationToken)
    {
        await _compilationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureBaseline();
            RequireAuthority();
            await LoadPolicyAsync(cancellationToken).ConfigureAwait(false);
            var (policy, fileRejected) = await ReadDesiredPolicyAsync(cancellationToken).ConfigureAwait(false);
            var state = _registry.State;
            var desired = new PolicyRevision(checked((_acceptedPolicy?.Revision ?? 0) + 1), NoConfPolicyFile.Encode(policy), _clock.GetUtcNow(), "file-watcher", "file");
            if (Compiled?.DesiredRevision == state.Revision && _appliedPolicyRevision == _acceptedPolicy?.Revision && string.Equals(desired.Digest, _policyHash, StringComparison.Ordinal))
            {
                SetPolicyFailure(fileRejected);
                return;
            }
            var candidate = CompileDesiredPolicy(state, policy, ref fileRejected);
            if (fileRejected && _acceptedPolicy is { } retained) desired = retained;
            if (Compiled?.DesiredRevision == state.Revision && _appliedPolicyRevision == desired.Revision && string.Equals(desired.Digest, _policyHash, StringComparison.Ordinal))
            {
                SetPolicyFailure(fileRejected);
                return;
            }
            if (_registry.State.Revision != state.Revision) return;
            if (_acceptedPolicy is null || !string.Equals(desired.Digest, _acceptedPolicy.Digest, StringComparison.Ordinal))
            {
                if (!await CommitPolicyAsync(desired, state.Revision, cancellationToken).ConfigureAwait(false)) return;
            }
            if (InstallPolicy(candidate, state, _acceptedPolicy ?? throw new InvalidOperationException("Accepted policy is missing."))) SetPolicyFailure(fileRejected);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or JsonException or IOException)
        {
            // Keep the accepted snapshot; volatile eligibility continues to enforce expiry and revocation.
            if (Failure.Length == 0) InvalidPolicy(_logger, exception);
            Volatile.Write(ref _failure, "Noconf revision rejected; the last accepted snapshot is retained.");
        }
        finally { _compilationGate.Release(); }
    }

    private void EnsureBaseline()
    {
        _baseline ??= _store.Snapshot;
        _version = Math.Max(_version, _store.Snapshot.Version);
    }

    public override void Dispose()
    {
        base.Dispose();
        _compilationGate.Dispose();
    }

    private void InvalidateChangedProofs(CompiledNoConfSnapshot applied, RegistryState state)
    {
        var previous = Compiled;
        var oldTransports = new Dictionary<string, string>(StringComparer.Ordinal);
        if (previous is not null)
            foreach (var service in previous.Services.Values)
                foreach (var upstream in service.Route.Upstreams)
                    if (upstream.Membership is { } identity) oldTransports.Add(identity.Partition, upstream.Identity);
        foreach (var service in applied.Services.Values)
            foreach (var upstream in service.Route.Upstreams)
                if (upstream.Membership is { } identity && oldTransports.TryGetValue(identity.Partition, out var old) && !string.Equals(old, upstream.Identity, StringComparison.Ordinal))
                    _availability.InvalidateReadiness(identity);
        foreach (var intent in state.Instances.Values) _availability.ClearPublication(intent.Identity);
    }

    private async ValueTask ProbeAndPublishAsync(CancellationToken cancellationToken)
    {
        var compiled = Compiled;
        if (compiled is null) return;
        var state = _registry.State;
        var work = new List<ProbeWork>();
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in compiled.Services.Values)
            foreach (var upstream in service.Route.Upstreams)
                if (upstream.Membership is { } identity && state.Instances.TryGetValue(identity.InstanceId, out var intent) && intent.Identity == identity)
                {
                    var status = _availability.Status(identity);
                    var key = identity.Partition + "|" + status.ReadinessGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    retained.Add(key);
                    if (status.LeaseValid && !intent.Draining) work.Add(new ProbeWork(intent, upstream, status.ReadinessGeneration, key));
                }
        foreach (var key in _trackers.Keys)
            if (!retained.Contains(key)) _trackers.TryRemove(key, out _);
        await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = cancellationToken },
            async (workItem, token) => await ProbeInstanceAsync(workItem, compiled, token).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async ValueTask ProbeInstanceAsync(ProbeWork work, CompiledNoConfSnapshot compiled, CancellationToken cancellationToken)
    {
        var intent = work.Intent;
        var successful = await _readiness.CheckAsync(intent, work.Upstream, cancellationToken).ConfigureAwait(false);
        var tracker = _trackers.GetOrAdd(work.Key, static _ => new ReadinessTracker());
        var ready = tracker.Record(intent.Identity, successful);
        if (!_availability.SetReadiness(intent.Identity, ready, TimeSpan.FromSeconds(30), work.Generation)) return;
        if (!ready || !ReferenceEquals(compiled, Compiled)) return;
        var gateway = _gateway.ReadPublicationProof();
        if (gateway is null)
        {
            PublishCurrent(work, compiled, new DestinationPublication(compiled.DesiredRevision, 0, false, false, _clock.GetUtcNow().AddSeconds(1)));
            return;
        }
        var host = compiled.Services[intent.Identity.ServiceId].Route.Host;
        var dns = await _dns.VerifyAsync(host, cancellationToken).ConfigureAwait(false);
        var until = _clock.GetUtcNow().Add(dns.Validity);
        if (until > gateway.ValidUntilUtc) until = gateway.ValidUntilUtc;
        if (!ReferenceEquals(compiled, Compiled)) return;
        PublishCurrent(work, compiled, new DestinationPublication(compiled.DesiredRevision, gateway.Generation, dns.Verified, true, until));
    }

    private void PublishCurrent(ProbeWork work, CompiledNoConfSnapshot compiled, DestinationPublication publication)
    {
        lock (_publicationGate)
            if (ReferenceEquals(compiled, Compiled)) _availability.SetPublication(work.Intent.Identity, publication, work.Generation);
    }

    private sealed record ProbeWork(InstanceIntent Intent, RuntimeUpstream Upstream, long Generation, string Key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Noconf revision failed validation; retaining the accepted snapshot.")]
    private static partial void InvalidPolicy(ILogger logger, Exception exception);
}
