using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
#pragma warning disable CA1416
using System.Collections.Concurrent;
using System.Net;
using System.Net.Quic;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Application.DAL.Observability;
using Mk8.Drava.Application.INF.Observability;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;
using Mk8.Drava.Application.INF.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Hosting;
internal sealed partial class ProxyListenerService : BackgroundService, IProxyListenerReloadApplier
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    private readonly IRouteMatcher _routeMatcher;
    private readonly IUpstreamSelector _upstreamSelector;
    private readonly UpstreamHealthStore _healthStore;
    private readonly ProxyForwarder _forwarder;
    private readonly UpgradeForwarder _upgradeForwarder;
    private readonly UpgradeRequestPolicy _upgradeRequestPolicy;
    private readonly ForwardedHeadersPolicy _forwardedHeadersPolicy;
    private readonly ProxyRouteActionPolicy _routeActionPolicy;
    private readonly PathRewritePolicy _pathRewritePolicy;
    private readonly ResponseCacheStore _cacheStore;
    private readonly Http3AltSvcPolicy _altSvcPolicy;
    private readonly CircuitBreakerStore _circuitBreakerStore;
    private readonly AcmeHttp01ChallengeResponder _acmeChallengeResponder;
    private readonly TlsConnectionAuthenticator _tlsAuthenticator;
    private readonly IHttp3QuicListenerFactory _quicListenerFactory;
    private readonly ProxyMetrics _metrics;
    private readonly RequestIdGenerator _requestIdGenerator;
    private readonly AccessLogEmitter _accessLogEmitter;
    private readonly ProxyAdmissionController _admission;
    private readonly ProxyShutdownCoordinator _shutdown;
    private readonly UpstreamConnectionPool _upstreamConnectionPool;
    private readonly Http3UpstreamConnectionPool _http3UpstreamConnectionPool;
    private readonly ClientRateLimiter _rateLimiter;
    private readonly ProxyRuntimeState _runtimeState;
    private readonly ProxyListenerReloadPlanner _reloadPlanner;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProxyListenerService> _logger;
    private readonly ILogger<ClientConnection> _connectionLogger;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly CancellationTokenSource _serviceStopping = new();
    private readonly Dictionary<string, ManagedListener> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ManagedQuicListener> _quicListeners = new(StringComparer.OrdinalIgnoreCase);
    public ProxyListenerService(IProxyActiveConfigurationSnapshotReader configurationStore, IRouteMatcher routeMatcher, IUpstreamSelector upstreamSelector, UpstreamHealthStore healthStore, ProxyForwarder forwarder, UpgradeForwarder upgradeForwarder, UpgradeRequestPolicy upgradeRequestPolicy, ForwardedHeadersPolicy forwardedHeadersPolicy, ProxyRouteActionPolicy routeActionPolicy, PathRewritePolicy pathRewritePolicy, ResponseCacheStore cacheStore, Http3AltSvcPolicy altSvcPolicy, CircuitBreakerStore circuitBreakerStore, AcmeHttp01ChallengeResponder acmeChallengeResponder, TlsConnectionAuthenticator tlsAuthenticator, IHttp3QuicListenerFactory quicListenerFactory, ProxyMetrics metrics, RequestIdGenerator requestIdGenerator, AccessLogEmitter accessLogEmitter, ProxyAdmissionController admission, ProxyShutdownCoordinator shutdown, UpstreamConnectionPool upstreamConnectionPool, Http3UpstreamConnectionPool http3UpstreamConnectionPool, ClientRateLimiter rateLimiter, ProxyRuntimeState runtimeState, ProxyListenerReloadPlanner reloadPlanner, TimeProvider timeProvider, ILogger<ProxyListenerService> logger, ILogger<ClientConnection> connectionLogger)
    {
        _configurationStore = configurationStore;
        _routeMatcher = routeMatcher;
        _upstreamSelector = upstreamSelector;
        _healthStore = healthStore;
        _forwarder = forwarder;
        _upgradeForwarder = upgradeForwarder;
        _upgradeRequestPolicy = upgradeRequestPolicy;
        _forwardedHeadersPolicy = forwardedHeadersPolicy;
        _routeActionPolicy = routeActionPolicy;
        _pathRewritePolicy = pathRewritePolicy;
        _cacheStore = cacheStore;
        _altSvcPolicy = altSvcPolicy;
        _circuitBreakerStore = circuitBreakerStore;
        _acmeChallengeResponder = acmeChallengeResponder;
        _tlsAuthenticator = tlsAuthenticator;
        _quicListenerFactory = quicListenerFactory;
        _metrics = metrics;
        _requestIdGenerator = requestIdGenerator;
        _accessLogEmitter = accessLogEmitter;
        _admission = admission;
        _shutdown = shutdown;
        _upstreamConnectionPool = upstreamConnectionPool;
        _http3UpstreamConnectionPool = http3UpstreamConnectionPool;
        _rateLimiter = rateLimiter;
        _runtimeState = runtimeState;
        _reloadPlanner = reloadPlanner;
        _timeProvider = timeProvider;
        _logger = logger;
        _connectionLogger = connectionLogger;
    }

    public async ValueTask<ProxyListenerReloadResult> ApplyReloadAsync(ProxyConfigurationSnapshot snapshot, Func<ProxyConfigurationSnapshot, ProxyConfigurationSnapshot> activateSnapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activateSnapshot);
        ArgumentNullException.ThrowIfNull(snapshot);
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _metrics.ListenerReloadAttempted();
            var attemptedAt = _timeProvider.GetUtcNow();
            var plan = CreateListenerReloadPlan(snapshot);
            var nextListeners = plan.DesiredTcpListeners;
            var nextQuicListeners = plan.DesiredQuicListeners;
            var diff = plan.TcpDiff;
            var quicDiff = plan.QuicDiff;
            Dictionary<string, ManagedListener> pending = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ManagedQuicListener> pendingQuic = new(StringComparer.OrdinalIgnoreCase);
            List<string> listenerErrors = [];
            try
            {
                foreach (var key in diff.Added.Concat(diff.Changed))
                {
                    var listener = nextListeners[key];
                    var handle = ManagedListener.Bind(listener, _timeProvider);
                    pending.Add(key, handle);
                    if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information))
                    {
                        LogProxyListenerPreparedOn10041(_logger, listener.Name, listener.Address, listener.Port, null);
                    }
                }
            }
            catch (Exception exception)when (exception is SocketException or IOException or InvalidOperationException)
            {
                foreach (var handle in pending.Values)
                {
                    await handle.DisposeWithoutDrainAsync().ConfigureAwait(false);
                }

                _metrics.ListenerStartFailed();
                _metrics.ListenerReloadFailed();
                var result = BuildReloadResult(ProxyListenerReloadApplicationState.Failed, attemptedAt, diff, quicDiff, pending, pendingQuic, [SafeError(exception)], plan.CurrentTcpListeners, plan.CurrentQuicListeners);
                UpdateRuntimeState(result);
                if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                {
                    LogProxyListenerReloadFailedWhile10042(_logger, exception);
                }
                return result;
            }

            foreach (var key in quicDiff.Added.Concat(quicDiff.Changed))
            {
                var listener = nextQuicListeners[key];
                try
                {
                    var handle = await ManagedQuicListener.BindAsync(listener, snapshot, _quicListenerFactory, _timeProvider, cancellationToken).ConfigureAwait(false);
                    pendingQuic.Add(key, handle);
                    _metrics.QuicListenerStarted();
                    if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information))
                    {
                        LogHTTPQUICListenerPreparedOn10043(_logger, listener.Name, listener.Address, listener.Port, null);
                    }
                }
                catch (Exception exception)when (exception is QuicException or SocketException or IOException or InvalidOperationException or PlatformNotSupportedException)
                {
                    _metrics.QuicListenerStartFailed();
                    _metrics.ListenerStartFailed();
                    var error = $"quic:{listener.Name}:{SafeError(exception)}";
                    listenerErrors.Add(error);
                    pendingQuic.Add(key, ManagedQuicListener.Failed(listener, SafeError(exception), _timeProvider));
                    if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
                    {
                        LogHTTPQUICListenerFailedTo10044(_logger, listener.Name, exception);
                    }
                }
            }

            ProxyConfigurationSnapshot activeSnapshot;
            try
            {
                activeSnapshot = activateSnapshot(snapshot);
            }
            catch (Exception exception)
            {
                foreach (var handle in pending.Values)
                {
                    await handle.DisposeWithoutDrainAsync().ConfigureAwait(false);
                }

                foreach (var handle in pendingQuic.Values)
                {
                    await handle.DisposeWithoutDrainAsync().ConfigureAwait(false);
                }

                _metrics.ListenerReloadFailed();
                var result = BuildReloadResult(ProxyListenerReloadApplicationState.Failed, attemptedAt, diff, quicDiff, pending, pendingQuic, [SafeError(exception)], plan.CurrentTcpListeners, plan.CurrentQuicListeners);
                UpdateRuntimeState(result);
                return result;
            }

            List<ManagedListener> oldHandles = [];
            List<ManagedQuicListener> oldQuicHandles = [];
            lock (_listeners)
            {
                foreach (var key in diff.Unchanged)
                {
                    _listeners[key].Update(nextListeners[key]);
                }

                foreach (var key in diff.Changed)
                {
                    if (_listeners.TryGetValue(key, out var old))
                    {
                        oldHandles.Add(old);
                    }

                    _listeners[key] = pending[key];
                }

                foreach (var key in diff.Added)
                {
                    _listeners[key] = pending[key];
                }

                foreach (var key in diff.Removed)
                {
                    if (_listeners.Remove(key, out var old))
                    {
                        oldHandles.Add(old);
                    }
                }
            }

            lock (_quicListeners)
            {
                foreach (var key in quicDiff.Unchanged)
                {
                    _quicListeners[key].Update(nextQuicListeners[key]);
                }

                foreach (var key in quicDiff.Changed)
                {
                    if (_quicListeners.TryGetValue(key, out var old))
                    {
                        oldQuicHandles.Add(old);
                    }

                    _quicListeners[key] = pendingQuic[key];
                }

                foreach (var key in quicDiff.Added)
                {
                    _quicListeners[key] = pendingQuic[key];
                }

                foreach (var key in quicDiff.Removed)
                {
                    if (_quicListeners.Remove(key, out var old))
                    {
                        oldQuicHandles.Add(old);
                    }
                }
            }

            foreach (var key in diff.Added.Concat(diff.Changed))
            {
                pending[key].Activate(this, _serviceStopping.Token);
            }

            foreach (var key in quicDiff.Added.Concat(quicDiff.Changed))
            {
                pendingQuic[key].Activate(this, _serviceStopping.Token);
            }

            foreach (var old in oldHandles)
            {
                await old.StopAcceptingAsync(activeSnapshot.Limits.ShutdownGracePeriod, cancellationToken).ConfigureAwait(false);
                _metrics.ListenerDrained();
            }

            foreach (var old in oldQuicHandles)
            {
                await old.StopAcceptingAsync(activeSnapshot.Limits.ShutdownGracePeriod, cancellationToken).ConfigureAwait(false);
                _metrics.ListenerDrained();
            }

            var success = BuildReloadResult(ProxyListenerReloadApplicationState.Applied, attemptedAt, diff, quicDiff, pending, pendingQuic, listenerErrors, plan.CurrentTcpListeners, plan.CurrentQuicListeners);
            _metrics.ListenerReloadSucceeded(diff.Added.Count + quicDiff.Added.Count, diff.Removed.Count + quicDiff.Removed.Count, diff.Changed.Count + quicDiff.Changed.Count, diff.Unchanged.Count + quicDiff.Unchanged.Count);
            UpdateRuntimeState(success);
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information))
            {
                LogProxyListenerReloadAppliedAdded10045(_logger, diff.Added.Count + quicDiff.Added.Count, diff.Removed.Count + quicDiff.Removed.Count, diff.Changed.Count + quicDiff.Changed.Count, diff.Unchanged.Count + quicDiff.Unchanged.Count, null);
            }
            return success;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public IReadOnlyList<ProxyListenerStatus> Snapshot()
    {
        List<ProxyListenerStatus> statuses = [];
        lock (_listeners)
        {
            statuses.AddRange(_listeners.Values.Select(static listener => listener.Snapshot()));
        }

        lock (_quicListeners)
        {
            statuses.AddRange(_quicListeners.Values.Select(static listener => listener.Snapshot()));
        }

        return statuses.OrderBy(static listener => listener.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static listener => listener.Kind, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _serviceStopping.Cancel();
        var snapshot = _configurationStore.Snapshot;
        var shutdownToken = _shutdown.BeginShutdown(snapshot.Limits.ShutdownGracePeriod);
        if (_shutdown.StartedAtUtc is not null && _shutdown.DeadlineUtc is not null)
        {
            _runtimeState.MarkShuttingDown(_shutdown.StartedAtUtc.Value, _shutdown.DeadlineUtc.Value);
        }

        ManagedListener[] listeners;
        ManagedQuicListener[] quicListeners;
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_listeners)
            {
                listeners = _listeners.Values.ToArray();
                _listeners.Clear();
            }

            lock (_quicListeners)
            {
                quicListeners = _quicListeners.Values.ToArray();
                _quicListeners.Clear();
            }
        }
        finally
        {
            _reloadGate.Release();
        }

        foreach (var listener in listeners)
        {
            await listener.StopAcceptingAsync(snapshot.Limits.ShutdownGracePeriod, shutdownToken).ConfigureAwait(false);
        }

        foreach (var listener in quicListeners)
        {
            await listener.StopAcceptingAsync(snapshot.Limits.ShutdownGracePeriod, shutdownToken).ConfigureAwait(false);
        }

        UpdateRuntimeState(null);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        _upstreamConnectionPool.Dispose();
        _http3UpstreamConnectionPool.Dispose();
    }

    private ListenerReloadPlan CreateListenerReloadPlan(ProxyConfigurationSnapshot snapshot)
    {
        var desiredTcpListeners = snapshot.Listeners.Where(static listener => listener.Enabled && listener.TcpTrafficEnabled).ToDictionary(static listener => RuntimeListenerIdentity.From(listener).Key, StringComparer.OrdinalIgnoreCase);
        var desiredQuicListeners = snapshot.Listeners.Where(static listener => listener.Enabled && listener.Http3.EnabledForTraffic).ToDictionary(static listener => listener.QuicIdentity!.Key, StringComparer.OrdinalIgnoreCase);
        var currentTcpListeners = SnapshotTcpListeners();
        var currentQuicListeners = SnapshotQuicListeners();
        var reloadPlan = _reloadPlanner.CreatePlan(ToTcpReloadTargets(currentTcpListeners), ToTcpReloadTargets(desiredTcpListeners), ToQuicReloadTargets(currentQuicListeners), ToQuicReloadTargets(desiredQuicListeners));
        return new ListenerReloadPlan(desiredTcpListeners, desiredQuicListeners, currentTcpListeners, currentQuicListeners, reloadPlan.TcpDiff, reloadPlan.QuicDiff);
    }

    private Dictionary<string, ManagedListener> SnapshotTcpListeners()
    {
        lock (_listeners)
        {
            return new Dictionary<string, ManagedListener>(_listeners, StringComparer.OrdinalIgnoreCase);
        }
    }

    private Dictionary<string, ManagedQuicListener> SnapshotQuicListeners()
    {
        lock (_quicListeners)
        {
            return new Dictionary<string, ManagedQuicListener>(_quicListeners, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, ProxyTcpListenerReloadTarget> ToTcpReloadTargets(IReadOnlyDictionary<string, RuntimeListener> desiredListeners)
    {
        Dictionary<string, ProxyTcpListenerReloadTarget> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (var(key, listener)in desiredListeners)
        {
            targets[key] = ToTcpReloadTarget(key, listener);
        }

        return targets;
    }

    private static Dictionary<string, ProxyTcpListenerReloadTarget> ToTcpReloadTargets(IReadOnlyDictionary<string, ManagedListener> currentListeners)
    {
        Dictionary<string, ProxyTcpListenerReloadTarget> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (var(key, handle)in currentListeners)
        {
            targets[key] = ToTcpReloadTarget(key, handle.Listener);
        }

        return targets;
    }

    private static ProxyTcpListenerReloadTarget ToTcpReloadTarget(string key, RuntimeListener listener)
    {
        return new ProxyTcpListenerReloadTarget(key, listener.Address, listener.Port, RuntimeListenerTransportText.FromTransport(listener.Transport));
    }

    private static Dictionary<string, ProxyQuicListenerReloadTarget> ToQuicReloadTargets(IReadOnlyDictionary<string, RuntimeListener> desiredListeners)
    {
        Dictionary<string, ProxyQuicListenerReloadTarget> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (var(key, listener)in desiredListeners)
        {
            targets[key] = ToQuicReloadTarget(key, listener, failed: false);
        }

        return targets;
    }

    private static Dictionary<string, ProxyQuicListenerReloadTarget> ToQuicReloadTargets(IReadOnlyDictionary<string, ManagedQuicListener> currentListeners)
    {
        Dictionary<string, ProxyQuicListenerReloadTarget> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (var(key, handle)in currentListeners)
        {
            targets[key] = ToQuicReloadTarget(key, handle.Listener, handle.State == ProxyListenerState.Failed);
        }

        return targets;
    }

    private static ProxyQuicListenerReloadTarget ToQuicReloadTarget(string key, RuntimeListener listener, bool failed)
    {
        return new ProxyQuicListenerReloadTarget(key, listener.Address, listener.Port, RuntimeListenerTransportText.FromTransport(listener.Transport), listener.Http3Enablement.ToConfigText(), failed);
    }

    private ProxyListenerReloadResult BuildReloadResult(ProxyListenerReloadApplicationState applicationState, DateTimeOffset attemptedAt, ProxyListenerDiff diff, ProxyListenerDiff quicDiff, IReadOnlyDictionary<string, ManagedListener> pending, IReadOnlyDictionary<string, ManagedQuicListener> pendingQuic, IReadOnlyList<string> errors, IReadOnlyDictionary<string, ManagedListener>? existing = null, IReadOnlyDictionary<string, ManagedQuicListener>? existingQuic = null)
    {
        List<ProxyListenerReloadChange> changes = [];
        existing ??= _listeners;
        existingQuic ??= _quicListeners;
        AddChanges(changes, "added", diff.Added, pending);
        AddChanges(changes, "removed", diff.Removed, existing);
        AddChanges(changes, "changed", diff.Changed, pending.Count == 0 ? _listeners : pending);
        AddChanges(changes, "unchanged", diff.Unchanged, existing);
        AddQuicChanges(changes, "added", quicDiff.Added, pendingQuic);
        AddQuicChanges(changes, "removed", quicDiff.Removed, existingQuic);
        AddQuicChanges(changes, "changed", quicDiff.Changed, pendingQuic.Count == 0 ? _quicListeners : pendingQuic);
        AddQuicChanges(changes, "unchanged", quicDiff.Unchanged, existingQuic);
        var orderedChanges = changes.OrderBy(static change => change.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static change => change.Action, StringComparer.OrdinalIgnoreCase).ToArray();
        var added = diff.Added.Count + quicDiff.Added.Count;
        var removed = diff.Removed.Count + quicDiff.Removed.Count;
        var changed = diff.Changed.Count + quicDiff.Changed.Count;
        var unchanged = diff.Unchanged.Count + quicDiff.Unchanged.Count;
        return applicationState switch
        {
            ProxyListenerReloadApplicationState.Applied => ProxyListenerReloadResult.Applied(attemptedAt, added, removed, changed, unchanged, orderedChanges, errors),
            ProxyListenerReloadApplicationState.Failed => ProxyListenerReloadResult.Failed(attemptedAt, added, removed, changed, unchanged, orderedChanges, errors),
            _ => throw new InvalidOperationException($"Unknown listener reload application state '{applicationState}'.")};
    }

    private enum ProxyListenerReloadApplicationState
    {
        Applied,
        Failed
    }

    private static void AddChanges(List<ProxyListenerReloadChange> changes, string action, IReadOnlyList<string> keys, IReadOnlyDictionary<string, ManagedListener> handles)
    {
        foreach (var key in keys)
        {
            if (!handles.TryGetValue(key, out var handle))
            {
                continue;
            }

            changes.Add(ProxyListenerReloadChange.FromStatus(action, handle.Snapshot()));
        }
    }

    private static void AddQuicChanges(List<ProxyListenerReloadChange> changes, string action, IReadOnlyList<string> keys, IReadOnlyDictionary<string, ManagedQuicListener> handles)
    {
        foreach (var key in keys)
        {
            if (!handles.TryGetValue(key, out var handle))
            {
                continue;
            }

            changes.Add(ProxyListenerReloadChange.FromStatus(action, handle.Snapshot()));
        }
    }

    private void UpdateRuntimeState(ProxyListenerReloadResult? lastReload)
    {
        var listeners = Snapshot();
        _metrics.SetActiveListeners(listeners.Count(static listener => listener.State == ProxyListenerState.Active));
        _metrics.SetActiveQuicListeners(listeners.Count(static listener => string.Equals(listener.Kind, "quic", StringComparison.Ordinal) && listener.State == ProxyListenerState.Active));
        _runtimeState.ReplaceListeners(listeners, lastReload);
    }

    private async Task AcceptLoopAsync(ManagedListener handle, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket clientSocket;
                try
                {
                    clientSocket = await handle.Socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _metrics.ConnectionAccepted();
                var requestSnapshot = _configurationStore.Snapshot;
                var requestListener = ResolveRequestListener(requestSnapshot, handle.Listener);
                var admission = _admission.AcquireClientConnection(requestSnapshot.Limits.MaxActiveClientConnections);
                if (admission is not ProxyAdmissionDecision.AcceptedResult acceptedAdmission)
                {
                    clientSocket.Dispose();
                    _metrics.ConnectionClosed();
                    continue;
                }

                var connectionTask = RunConnectionAsync(clientSocket, requestSnapshot, requestListener, acceptedAdmission.Lease, _shutdown.Token);
                handle.AddConnectionTask(connectionTask);
            }
        }
        catch (Exception exception)when (exception is SocketException or IOException)
        {
            handle.MarkFailed(SafeError(exception));
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogProxyListenerStoppedAfterSocket10046(_logger, handle.Listener.Name, exception);
            }
        }
        catch (Exception exception)
        {
            handle.MarkFailed(SafeError(exception));
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Error))
            {
                LogProxyListenerStoppedUnexpectedly10047(_logger, handle.Listener.Name, exception);
            }
        }
        finally
        {
            UpdateRuntimeState(null);
        }
    }

    private async Task AcceptQuicLoopAsync(ManagedQuicListener handle, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && handle.ListenerHandle is not null)
            {
                QuicConnection connection;
                try
                {
                    connection = await handle.ListenerHandle.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _metrics.ConnectionAccepted();
                var requestSnapshot = _configurationStore.Snapshot;
                var requestListener = ResolveRequestListener(requestSnapshot, handle.Listener);
                var admission = _admission.AcquireClientConnection(requestSnapshot.Limits.MaxActiveClientConnections);
                if (admission is not ProxyAdmissionDecision.AcceptedResult acceptedAdmission)
                {
                    _metrics.ConnectionClosed();
                    await connection.CloseAsync(0x100, CancellationToken.None).ConfigureAwait(false);
                    await connection.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                var connectionTask = RunQuicConnectionAsync(connection, requestSnapshot, requestListener, acceptedAdmission.Lease, _shutdown.Token);
                handle.AddConnectionTask(connectionTask);
            }
        }
        catch (Exception exception)when (exception is QuicException or SocketException or IOException)
        {
            handle.MarkFailed(SafeError(exception));
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogHTTPQUICListenerStoppedAfter10048(_logger, handle.Listener.Name, exception);
            }
        }
        catch (Exception exception)
        {
            handle.MarkFailed(SafeError(exception));
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Error))
            {
                LogHTTPQUICListenerStoppedUnexpectedly10049(_logger, handle.Listener.Name, exception);
            }
        }
        finally
        {
            UpdateRuntimeState(null);
        }
    }

    private async Task RunConnectionAsync(Socket clientSocket, ProxyConfigurationSnapshot snapshot, RuntimeListener listener, AdmissionLease admissionLease, CancellationToken cancellationToken)
    {
        using var ownedAdmission = admissionLease;
        try
        {
            var connection = new ClientConnection(clientSocket, snapshot, listener, _routeMatcher, _upstreamSelector, _healthStore, _forwarder, _upgradeForwarder, _upgradeRequestPolicy, _forwardedHeadersPolicy, _routeActionPolicy, _pathRewritePolicy, _cacheStore, _altSvcPolicy, _circuitBreakerStore, _acmeChallengeResponder, _tlsAuthenticator, _metrics, _requestIdGenerator, _accessLogEmitter, _rateLimiter, _timeProvider, _connectionLogger);
            await connection.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)when (exception is SocketException or IOException)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogClientConnectionEndedWithAn10050(_logger, exception);
            }
        }
        catch (Exception exception)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Error))
            {
                LogClientConnectionFailedUnexpectedly10051(_logger, exception);
            }
        }
        finally
        {
            _metrics.ConnectionClosed();
        }
    }

    private async Task RunQuicConnectionAsync(QuicConnection connection, ProxyConfigurationSnapshot snapshot, RuntimeListener listener, AdmissionLease admissionLease, CancellationToken cancellationToken)
    {
        using var ownedAdmission = admissionLease;
        try
        {
            var http3Connection = new Http3Connection(connection, snapshot, listener, _routeMatcher, _upstreamSelector, _healthStore, _forwarder, _forwardedHeadersPolicy, _routeActionPolicy, _pathRewritePolicy, _cacheStore, _circuitBreakerStore, _acmeChallengeResponder, _metrics, _requestIdGenerator, _accessLogEmitter, _rateLimiter, _timeProvider, _connectionLogger);
            await http3Connection.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)when (exception is QuicException or IOException)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                LogHTTPClientConnectionEndedWith10052(_logger, exception);
            }
        }
        catch (Exception exception)
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Error))
            {
                LogHTTPClientConnectionFailedUnexpectedly10053(_logger, exception);
            }
        }
        finally
        {
            _metrics.ConnectionClosed();
        }
    }

    private static RuntimeListener ResolveRequestListener(ProxyConfigurationSnapshot requestSnapshot, RuntimeListener boundListener)
    {
        var current = requestSnapshot.Listeners.FirstOrDefault(listener => string.Equals(listener.Name, boundListener.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(listener.Address, boundListener.Address, StringComparison.OrdinalIgnoreCase) && listener.Port == boundListener.Port && listener.Transport == boundListener.Transport);
        return current ?? boundListener;
    }

    private static string SafeError(Exception exception)
    {
        return exception switch
        {
            SocketException socket => socket.SocketErrorCode.ToString(),
            QuicException => "quic_error",
            IOException => "io_error",
            PlatformNotSupportedException => "platform_not_supported",
            InvalidOperationException => "invalid_operation",
            _ => exception.GetType().Name
        };
    }

    private sealed record ListenerReloadPlan(IReadOnlyDictionary<string, RuntimeListener> DesiredTcpListeners, IReadOnlyDictionary<string, RuntimeListener> DesiredQuicListeners, IReadOnlyDictionary<string, ManagedListener> CurrentTcpListeners, IReadOnlyDictionary<string, ManagedQuicListener> CurrentQuicListeners, ProxyListenerDiff TcpDiff, ProxyListenerDiff QuicDiff);
    private sealed class ManagedListener
    {
        private readonly ConcurrentDictionary<Task, byte> _connectionTasks = new();
        private readonly Lock _gate = new();
        private readonly TimeProvider _timeProvider;
        private RuntimeListener _listener;
        private ProxyListenerState _state = ProxyListenerState.Starting;
        private DateTimeOffset? _startedAtUtc;
        private DateTimeOffset? _stoppedAtUtc;
        private string? _lastError;
        private Task? _acceptTask;
        private ManagedListener(RuntimeListener listener, Socket socket, TimeProvider timeProvider)
        {
            _listener = listener;
            Socket = socket;
            _timeProvider = timeProvider;
        }

        public RuntimeListener Listener
        {
            get
            {
                lock (_gate)
                {
                    return _listener;
                }
            }
        }

        public Socket Socket { get; }

        public static ManagedListener Bind(RuntimeListener listener, TimeProvider timeProvider)
        {
            var listenAddress = IPAddress.Parse(listener.Address);
            var listenEndPoint = new IPEndPoint(listenAddress, listener.Port);
            var socket = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                ExclusiveAddressUse = false
            };
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(listenEndPoint);
                socket.Listen(listener.Backlog);
                return new ManagedListener(listener, socket, timeProvider);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public void Activate(ProxyListenerService owner, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_acceptTask is not null)
                {
                    return;
                }

                _state = ProxyListenerState.Active;
                _startedAtUtc = _timeProvider.GetUtcNow();
                _stoppedAtUtc = null;
                _lastError = null;
                _acceptTask = Task.Run(() => owner.AcceptLoopAsync(this, cancellationToken), CancellationToken.None);
            }
        }

        public void Update(RuntimeListener listener)
        {
            lock (_gate)
            {
                _listener = listener;
            }
        }

        public void AddConnectionTask(Task task)
        {
            _connectionTasks.TryAdd(task, 0);
            _ = task.ContinueWith(completed => _connectionTasks.TryRemove(completed, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public async ValueTask StopAcceptingAsync(TimeSpan drainGracePeriod, CancellationToken cancellationToken)
        {
            Task? acceptTask;
            lock (_gate)
            {
                if (_state is not ProxyListenerState.Stopped and not ProxyListenerState.Failed)
                {
                    _state = ProxyListenerState.Draining;
                }

                acceptTask = _acceptTask;
            }

            Socket.Dispose();
            if (acceptTask is not null)
            {
                try
                {
                    await acceptTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
                {
                }
            }

            var activeTasks = _connectionTasks.Keys.ToArray();
            if (activeTasks.Length > 0)
            {
                var allConnections = Task.WhenAll(activeTasks);
                var timeout = Task.Delay(drainGracePeriod, _timeProvider, cancellationToken);
                await Task.WhenAny(allConnections, timeout).ConfigureAwait(false);
            }

            lock (_gate)
            {
                _state = ProxyListenerState.Stopped;
                _stoppedAtUtc = _timeProvider.GetUtcNow();
            }
        }

        public ValueTask DisposeWithoutDrainAsync()
        {
            Socket.Dispose();
            lock (_gate)
            {
                _state = ProxyListenerState.Stopped;
                _stoppedAtUtc = _timeProvider.GetUtcNow();
            }

            return ValueTask.CompletedTask;
        }

        public void MarkFailed(string error)
        {
            lock (_gate)
            {
                _state = ProxyListenerState.Failed;
                _stoppedAtUtc = _timeProvider.GetUtcNow();
                _lastError = error;
            }
        }

        public ProxyListenerStatus Snapshot()
        {
            lock (_gate)
            {
                var identity = RuntimeListenerIdentity.From(_listener);
                return new ProxyListenerStatus(_listener.Name, identity.Key, identity.BindKey, "tcp", _listener.Address, _listener.Port, RuntimeListenerTransportScheme.FromTransport(_listener.Transport), identity.TlsEnabled, _listener.Protocols.ToConfigText(), _listener.Http3.ToStatus(), _listener.Http2Limits.MaxConcurrentStreams, _listener.Http2Limits.MaxHeaderListBytes, _listener.Http2Limits.MaxFrameSize, _state, _connectionTasks.Count, _startedAtUtc, _stoppedAtUtc, _lastError);
            }
        }
    }

    private sealed class ManagedQuicListener
    {
        private readonly ConcurrentDictionary<Task, byte> _connectionTasks = new();
        private readonly Lock _gate = new();
        private readonly TimeProvider _timeProvider;
        private RuntimeListener _listener;
        private ProxyListenerState _state = ProxyListenerState.Starting;
        private DateTimeOffset? _startedAtUtc;
        private DateTimeOffset? _stoppedAtUtc;
        private string? _lastError;
        private Task? _acceptTask;
        private ManagedQuicListener(RuntimeListener listener, QuicListener? listenerHandle, TimeProvider timeProvider)
        {
            _listener = listener;
            ListenerHandle = listenerHandle;
            _timeProvider = timeProvider;
        }

        public RuntimeListener Listener
        {
            get
            {
                lock (_gate)
                {
                    return _listener;
                }
            }
        }

        public ProxyListenerState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public QuicListener? ListenerHandle { get; }

        public static async ValueTask<ManagedQuicListener> BindAsync(RuntimeListener listener, ProxyConfigurationSnapshot snapshot, IHttp3QuicListenerFactory factory, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            var listenerHandle = await factory.ListenAsync(listener, snapshot, cancellationToken).ConfigureAwait(false);
            return new ManagedQuicListener(listener, listenerHandle, timeProvider);
        }

        public static ManagedQuicListener Failed(RuntimeListener listener, string error, TimeProvider timeProvider)
        {
            var failed = new ManagedQuicListener(listener, null, timeProvider);
            failed.MarkFailed(error);
            return failed;
        }

        public void Activate(ProxyListenerService owner, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_acceptTask is not null || ListenerHandle is null || _state == ProxyListenerState.Failed)
                {
                    return;
                }

                _state = ProxyListenerState.Active;
                _startedAtUtc = _timeProvider.GetUtcNow();
                _stoppedAtUtc = null;
                _lastError = null;
                _acceptTask = Task.Run(() => owner.AcceptQuicLoopAsync(this, cancellationToken), CancellationToken.None);
            }
        }

        public void Update(RuntimeListener listener)
        {
            lock (_gate)
            {
                _listener = listener;
            }
        }

        public void AddConnectionTask(Task task)
        {
            _connectionTasks.TryAdd(task, 0);
            _ = task.ContinueWith(completed => _connectionTasks.TryRemove(completed, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public async ValueTask StopAcceptingAsync(TimeSpan drainGracePeriod, CancellationToken cancellationToken)
        {
            Task? acceptTask;
            lock (_gate)
            {
                if (_state is not ProxyListenerState.Stopped and not ProxyListenerState.Failed)
                {
                    _state = ProxyListenerState.Draining;
                }

                acceptTask = _acceptTask;
            }

            if (ListenerHandle is not null)
            {
                await ListenerHandle.DisposeAsync().ConfigureAwait(false);
            }

            if (acceptTask is not null)
            {
                try
                {
                    await acceptTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
                {
                }
            }

            var activeTasks = _connectionTasks.Keys.ToArray();
            if (activeTasks.Length > 0)
            {
                var allConnections = Task.WhenAll(activeTasks);
                var timeout = Task.Delay(drainGracePeriod, _timeProvider, cancellationToken);
                await Task.WhenAny(allConnections, timeout).ConfigureAwait(false);
            }

            lock (_gate)
            {
                _state = ProxyListenerState.Stopped;
                _stoppedAtUtc = _timeProvider.GetUtcNow();
            }
        }

        public async ValueTask DisposeWithoutDrainAsync()
        {
            if (ListenerHandle is not null)
            {
                await ListenerHandle.DisposeAsync().ConfigureAwait(false);
            }

            lock (_gate)
            {
                _state = ProxyListenerState.Stopped;
                _stoppedAtUtc = _timeProvider.GetUtcNow();
            }
        }

        public void MarkFailed(string error)
        {
            lock (_gate)
            {
                _state = ProxyListenerState.Failed;
                _stoppedAtUtc = _timeProvider.GetUtcNow();
                _lastError = error;
            }
        }

        public ProxyListenerStatus Snapshot()
        {
            lock (_gate)
            {
                var identity = RuntimeQuicListenerIdentity.From(_listener);
                return new ProxyListenerStatus(_listener.Name, identity.Key, identity.BindKey, "quic", _listener.Address, _listener.Port, "udp/quic", identity.TlsEnabled, _listener.Protocols.ToConfigText(), _listener.Http3.ToStatus(), _listener.Http2Limits.MaxConcurrentStreams, _listener.Http2Limits.MaxHeaderListBytes, _listener.Http2Limits.MaxFrameSize, _state, _connectionTasks.Count, _startedAtUtc, _stoppedAtUtc, _lastError);
            }
        }
    }
}
