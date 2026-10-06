using System.Collections.Concurrent;
using System.Security.Cryptography;
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
    private readonly ConcurrentDictionary<string, ReadinessTracker> _trackers = new(StringComparer.Ordinal);
    private ProxyConfigurationSnapshot? _baseline;
    private CompiledNoConfSnapshot? _compiled;
    private string _policyHash = "";
    private int _version;
    private string _failure = "";

    public NoConfReconciler(RegistryCoordinator registry, DestinationAvailabilityStore availability, ProxyConfigurationStore store,
        NoConfSnapshotCompiler compiler, IRegisteredReadinessProbe readiness, IServiceDnsVerifier dns, IGatewayPublicationSource gateway,
        string policyPath, string domain, string localNodeId, TimeProvider clock, ILogger<NoConfReconciler> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
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
        _policyPath = policyPath; _domain = domain; _localNodeId = localNodeId; _clock = clock; _logger = logger;
    }

    public CompiledNoConfSnapshot? Compiled => Volatile.Read(ref _compiled);
    public string Failure => Volatile.Read(ref _failure);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _baseline = _store.Snapshot;
        _version = _baseline.Version;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _clock);
        var nextProbe = _clock.GetTimestamp();
        var firstProbe = true;
        do
        {
            await CompileLatestAsync(stoppingToken).ConfigureAwait(false);
            if (_clock.GetElapsedTime(nextProbe) >= TimeSpan.FromSeconds(10) || firstProbe)
            {
                await ProbeAndPublishAsync(stoppingToken).ConfigureAwait(false);
                nextProbe = _clock.GetTimestamp();
                firstProbe = false;
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async ValueTask CompileLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var policy = await NoConfPolicyFile.ReadAsync(_policyPath, cancellationToken).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(policy)));
            var state = _registry.State;
            if (Compiled?.DesiredRevision == state.Revision && string.Equals(hash, _policyHash, StringComparison.Ordinal)) return;
            var candidate = _compiler.Compile(state, _baseline ?? throw new InvalidOperationException("Manual baseline is missing."), policy, _domain, _localNodeId);
            if (_registry.State.Revision != state.Revision) return;
            var snapshot = candidate.Snapshot.WithVersion(checked(++_version));
            var applied = new CompiledNoConfSnapshot(candidate.DesiredRevision, snapshot, candidate.Services);
            lock (_publicationGate)
            {
                InvalidateChangedProofs(applied, state);
                _store.Replace(snapshot);
                Volatile.Write(ref _compiled, applied);
            }
            _policyHash = hash;
            Volatile.Write(ref _failure, "");
            foreach (var intent in state.Instances.Values)
                if (!applied.Services.ContainsKey(intent.Identity.ServiceId)) _availability.ClearPublication(intent.Identity);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or JsonException or IOException)
        {
            // Keep the accepted snapshot; volatile eligibility continues to enforce expiry and revocation.
            if (Failure.Length == 0) InvalidPolicy(_logger, exception);
            Volatile.Write(ref _failure, "Noconf revision rejected; the last accepted snapshot is retained.");
        }
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
