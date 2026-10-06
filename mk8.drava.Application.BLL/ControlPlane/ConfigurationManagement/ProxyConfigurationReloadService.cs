using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public sealed class ProxyConfigurationReloadService : IProxyConfigurationReloadOperations<ProxyConfigurationProjection>, IProxyConfigurationValidationOperations
{
    private readonly IProxyConfigurationLoader _loader;
    private readonly IProxyActiveConfigurationSnapshotReader _snapshotReader;
    private readonly IProxyActiveConfigurationSnapshotWriter _snapshotWriter;
    private readonly ResponseCacheStore _cacheStore;
    private readonly IProxyConfigurationReloadMetricsSink _metrics;
    private readonly IProxyListenerReloadApplier _listenerReloadApplier;
    private readonly IProxyConfigurationReloadEventSink _events;
    private readonly IProxyConfigurationHttp3ProjectionSource _http3ProjectionSource;
    public ProxyConfigurationReloadService(IProxyConfigurationLoader loader, IProxyActiveConfigurationSnapshotReader snapshotReader, IProxyActiveConfigurationSnapshotWriter snapshotWriter, ResponseCacheStore cacheStore, IProxyConfigurationReloadMetricsSink metrics, IProxyListenerReloadApplier listenerReloadApplier, IProxyConfigurationReloadEventSink events, IProxyConfigurationHttp3ProjectionSource http3ProjectionSource)
    {
        _loader = loader;
        _snapshotReader = snapshotReader;
        _snapshotWriter = snapshotWriter;
        _cacheStore = cacheStore;
        _metrics = metrics;
        _listenerReloadApplier = listenerReloadApplier;
        _events = events;
        _http3ProjectionSource = http3ProjectionSource;
    }

    public async ValueTask<ProxyConfigurationReloadResult<ProxyConfigurationProjection>> ReloadAsync(CancellationToken cancellationToken)
    {
        var loadResult = await _loader.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loadResult is ProxyConfigurationLoadResult.FailedResult failedLoad)
        {
            _metrics.ConfigReloadFailed();
            _events.LoadFailed(failedLoad.SourceDirectory, failedLoad.Errors);
            var existing = ReadExistingSnapshot();
            return ProxyConfigurationReloadResult<ProxyConfigurationProjection>.LoadFailed(sourceDirectory: failedLoad.SourceDirectory, attemptedAtUtc: failedLoad.AttemptedAtUtc, activeVersion: existing?.Version, loadedAtUtc: existing?.LoadedAtUtc, discovery: failedLoad.Discovery, errors: failedLoad.Errors, fileErrors: failedLoad.FileErrors, activeConfiguration: existing is null ? null : ToProjection(existing));
        }

        var loaded = (ProxyConfigurationLoadResult.LoadedResult)loadResult;
        var listenerReload = await _listenerReloadApplier.ApplyReloadAsync(loaded.Snapshot, candidate => _snapshotWriter.Replace(candidate), cancellationToken).ConfigureAwait(false);
        if (listenerReload is ProxyListenerReloadResult.FailedResult)
        {
            _metrics.ConfigReloadFailed();
            var existing = ReadExistingSnapshot();
            return ProxyConfigurationReloadResult<ProxyConfigurationProjection>.ListenerReloadFailed(sourceDirectory: loaded.SourceDirectory, attemptedAtUtc: loaded.AttemptedAtUtc, activeVersion: existing?.Version, loadedAtUtc: existing?.LoadedAtUtc, discovery: loaded.Discovery, listenerReload: listenerReload, activeConfiguration: existing is null ? null : ToProjection(existing));
        }

        var snapshot = _snapshotReader.Snapshot;
        _metrics.ConfigReloadSucceeded();
        _cacheStore.Clear("reload");
        _events.Loaded(snapshot.Version, snapshot.SourceDirectory);
        return ProxyConfigurationReloadResult<ProxyConfigurationProjection>.Reloaded(sourceDirectory: snapshot.SourceDirectory, attemptedAtUtc: loaded.AttemptedAtUtc, activeVersion: snapshot.Version, loadedAtUtc: snapshot.LoadedAtUtc, discovery: loaded.Discovery, listenerReload: listenerReload, activeConfiguration: ToProjection(snapshot));
    }

    public async ValueTask<ProxyConfigurationValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var loadResult = await _loader.ValidateAsync(cancellationToken).ConfigureAwait(false);
        var existing = ReadExistingSnapshot();
        int? activeVersion = existing?.Version;
        var lastSuccessfulLoadAtUtc = existing?.LoadedAtUtc;
        if (loadResult is ProxyConfigurationLoadResult.ValidatedResult validated)
        {
            return ProxyConfigurationValidationResult.Valid(sourceDirectory: validated.SourceDirectory, attemptedAtUtc: validated.AttemptedAtUtc, activeVersion: activeVersion, lastSuccessfulLoadAtUtc: lastSuccessfulLoadAtUtc, wouldBeVersion: validated.WouldBeVersion, sourceFiles: validated.SourceFiles, discovery: validated.Discovery);
        }

        var failedValidation = (ProxyConfigurationLoadResult.FailedResult)loadResult;
        return ProxyConfigurationValidationResult.Invalid(sourceDirectory: failedValidation.SourceDirectory, attemptedAtUtc: failedValidation.AttemptedAtUtc, activeVersion: activeVersion, lastSuccessfulLoadAtUtc: lastSuccessfulLoadAtUtc, wouldBeVersion: failedValidation.WouldBeVersion, sourceFiles: failedValidation.SourceFiles, discovery: failedValidation.Discovery, errors: failedValidation.Errors, fileErrors: failedValidation.FileErrors);
    }

    private ProxyConfigurationSnapshot? ReadExistingSnapshot()
    {
        return _snapshotReader.ReadSnapshot()is ProxyConfigurationSnapshotReadResult.AvailableResult available ? available.Snapshot : null;
    }

    private ProxyConfigurationProjection ToProjection(ProxyConfigurationSnapshot snapshot)
    {
        return ProxyConfigurationProjectionMapper.ToProjection(snapshot, _http3ProjectionSource.Project(ProxyHttp3SupportConfigurationSourceMapper.FromSources(snapshot.Listeners, snapshot.Routes)));
    }
}
