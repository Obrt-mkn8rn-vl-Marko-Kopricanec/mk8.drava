using Supprocom.Secrets;

namespace Mk8.Drava.Configuration;

// Configuration remains the startup snapshot. Owned mutable slots share one completion boundary.
public sealed class PortableRole<T> : IAsyncDisposable where T : class
{
    private readonly ISecretDocumentUpdater _store;
    private readonly string _path;
    private readonly string _configurationDocument;
    private readonly Lock _gate = new();
    private readonly PortableRoleMutationQueue _mutations;
    private readonly Action<string, long, long> _synchronize;
    private string? _encodedMaterial;
    private string? _encodedArchive;
    private SecretBytes _material;
    private PortablePolicyDocuments _policyDocuments;
    private PortableManualConfigurationDocument _manualConfiguration;
    private bool _archiveRefreshAllowed = true;
    private bool _closing;
    private Task? _writeTask;
    public T Configuration { get; }
    private static string MaterialKey => "Drava:Material:" + typeof(T).Name;

    internal PortableRole(T configuration, ISecretDocumentUpdater store, string path,
        string configurationDocument, IReadOnlyDictionary<string, string> snapshot, Action<string, long, long>? synchronize = null)
    {
        Configuration = configuration;
        _store = store;
        _path = path;
        _configurationDocument = configurationDocument;
        snapshot.TryGetValue(MaterialKey, out _encodedMaterial);
        _material = _encodedMaterial is null ? SecretBytes.Empty
            : BootstrapFile.Parse<SecretBytes>(System.Text.Encoding.UTF8.GetBytes(_encodedMaterial));
        if (_material.Length > (128 * 1024) + 36) throw new InvalidDataException("Protected presentation material exceeds its slot bound.");
        snapshot.TryGetValue(InstallationCollectionBounds.PolicyArchiveKey, out _encodedArchive);
        snapshot.TryGetValue(InstallationCollectionBounds.PolicySourceKey, out var source);
        _policyDocuments = new PortablePolicyDocuments(source, _encodedArchive);
        snapshot.TryGetValue(InstallationCollectionBounds.ManualSourceKey, out var manual);
        _manualConfiguration = new PortableManualConfigurationDocument(manual ?? "");
        _synchronize = synchronize ?? InstallationDocumentGuard.Synchronize;
        _mutations = new PortableRoleMutationQueue();
    }

    public SecretBytes ReadMaterial()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            return _material;
        }
    }

    public ValueTask WriteMaterialAsync(SecretBytes material, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.Length is < 128 or > (128 * 1024) + 36) throw new InvalidDataException("Protected presentation material exceeds its slot bound.");
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_writeTask is { IsCompleted: false }) throw new InvalidOperationException("Protected material already has an active write.");
            _writeTask = _mutations.EnqueueAsync(token => WriteCoreAsync(material, token), cancellationToken);
            return new ValueTask(_writeTask);
        }
    }

    public PortablePolicyDocuments ReadPolicyDocuments()
    {
        RequirePolicyRole();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            return _policyDocuments;
        }
    }

    public PortableManualConfigurationDocument ReadManualConfiguration()
    {
        RequirePolicyRole();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            return _manualConfiguration;
        }
    }

    public ValueTask<PortableManualConfigurationDocument> RefreshManualConfigurationAsync(CancellationToken cancellationToken)
    {
        RequirePolicyRole();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            return new ValueTask<PortableManualConfigurationDocument>(_mutations.EnqueueAsync(RefreshManualCoreAsync, cancellationToken));
        }
    }

    private async Task<PortableManualConfigurationDocument> RefreshManualCoreAsync(CancellationToken cancellationToken)
    {
        await RefreshPolicyCoreAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate) return _manualConfiguration;
    }

    // Acquire Application writer ownership before the first refresh. Later refreshes only adopt configured source.
    public ValueTask<PortablePolicyDocuments> RefreshPolicyDocumentsAsync(CancellationToken cancellationToken)
    {
        RequirePolicyRole();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            return new ValueTask<PortablePolicyDocuments>(_mutations.EnqueueAsync(RefreshPolicyCoreAsync, cancellationToken));
        }
    }

    public ValueTask WritePolicyArchiveAsync(PortablePolicyArchive expected, PortablePolicyArchive replacement, CancellationToken cancellationToken)
    {
        RequirePolicyRole();
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        InstallationDocumentGuard.RequireDurabilitySupport();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (!expected.Equals(_policyDocuments.Archive)) throw new InvalidDataException("Protected policy staging used a stale archive snapshot.");
            return new ValueTask(_mutations.EnqueueAsync(token => WritePolicyCoreAsync(expected, replacement, token), cancellationToken));
        }
    }

    private async Task<bool> WriteCoreAsync(SecretBytes material, CancellationToken cancellationToken)
    {
        _archiveRefreshAllowed = false;
        if (!_policyDocuments.Archive.IsEmpty) InstallationDocumentGuard.RequireDurabilitySupport();
        RequireDocuments();
        var encoded = BootstrapFile.Serialize(material);
        await ProtectedConfigurationOperation.RunAsync(() => _store.UpdateDocumentAsync(settings => Transform(settings, MaterialKey, encoded), cancellationToken)).ConfigureAwait(false);
        RequireDocuments();
        if (!_policyDocuments.Archive.IsEmpty) SynchronizeDocuments();
        lock (_gate) { _encodedMaterial = encoded; _material = material; }
        return true;
    }

    private async Task<bool> WritePolicyCoreAsync(PortablePolicyArchive expected, PortablePolicyArchive replacement, CancellationToken cancellationToken)
    {
        if (!expected.Equals(_policyDocuments.Archive)) throw new InvalidDataException("Protected policy staging used a stale archive snapshot.");
        _archiveRefreshAllowed = false;
        RequireDocuments();
        await ProtectedConfigurationOperation.RunAsync(() => _store.UpdateDocumentAsync(settings => Transform(settings, InstallationCollectionBounds.PolicyArchiveKey, replacement.Reveal()), cancellationToken)).ConfigureAwait(false);
        RequireDocuments();
        SynchronizeDocuments();
        lock (_gate)
        {
            _encodedArchive = replacement.Reveal();
            _policyDocuments = new PortablePolicyDocuments(_policyDocuments.ConfiguredSource.Reveal(), _encodedArchive);
        }
        return true;
    }

    private async Task<PortablePolicyDocuments> RefreshPolicyCoreAsync(CancellationToken cancellationToken)
    {
        if (_store is not ISecretStore reader) throw new InvalidOperationException("Protected role storage cannot refresh its document.");
        RequireDocuments();
        var snapshot = await ProtectedConfigurationOperation.RunAsync(() => reader.LoadAsync(cancellationToken)).ConfigureAwait(false);
        RequireDocuments();
        InstallationCollectionBounds.RequireSettings([.. snapshot.Select(static setting => new SupprocomSecretSetting(setting.Key, setting.Value))]);
        snapshot.TryGetValue(PortableConfiguration.Key<T>(), out var role);
        snapshot.TryGetValue(MaterialKey, out var material);
        snapshot.TryGetValue(InstallationCollectionBounds.PolicyArchiveKey, out var archive);
        if (!string.Equals(role, _configurationDocument, StringComparison.Ordinal) || !string.Equals(material, _encodedMaterial, StringComparison.Ordinal)
            || (!_archiveRefreshAllowed && !string.Equals(archive, _encodedArchive, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Protected role configuration or owned material changed outside the startup owner.");
        }
        snapshot.TryGetValue(InstallationCollectionBounds.PolicySourceKey, out var source);
        var documents = new PortablePolicyDocuments(source, archive);
        snapshot.TryGetValue(InstallationCollectionBounds.ManualSourceKey, out var manual);
        var manualConfiguration = new PortableManualConfigurationDocument(manual ?? "");
        lock (_gate) { _encodedArchive = archive; _policyDocuments = documents; _manualConfiguration = manualConfiguration; }
        _archiveRefreshAllowed = false;
        return documents;
    }

    private List<SupprocomSecretSetting> Transform(IReadOnlyList<SupprocomSecretSetting> settings, string key, string encoded)
    {
        string? role = null;
        string? prior = null;
        string? archive = null;
        var replacement = new List<SupprocomSecretSetting>(settings.Count + 1);
        foreach (var setting in settings)
        {
            if (string.Equals(setting.Key, PortableConfiguration.Key<T>(), StringComparison.OrdinalIgnoreCase)) role = setting.Value;
            if (string.Equals(setting.Key, MaterialKey, StringComparison.OrdinalIgnoreCase)) prior = setting.Value;
            if (string.Equals(setting.Key, InstallationCollectionBounds.PolicyArchiveKey, StringComparison.OrdinalIgnoreCase)) archive = setting.Value;
            if (!string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase)) replacement.Add(setting);
        }
        if (!string.Equals(role, _configurationDocument, StringComparison.Ordinal) || !string.Equals(prior, _encodedMaterial, StringComparison.Ordinal)
            || !string.Equals(archive, _encodedArchive, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Protected role configuration or material changed outside the current startup owner.");
        }
        replacement.Add(new SupprocomSecretSetting(key, encoded));
        InstallationCollectionBounds.RequireSettings(replacement);
        return replacement;
    }

    private void RequireDocuments()
    {
        InstallationDocumentGuard.Require(_path, 1, InstallationCollectionBounds.MaximumDocumentBytes);
        InstallationDocumentGuard.Require(Path.Combine(Path.GetDirectoryName(_path)!, ".installation.key"), 32, 64);
    }

    private void SynchronizeDocuments()
    {
        _synchronize(_path, 1, InstallationCollectionBounds.MaximumDocumentBytes);
        _synchronize(Path.Combine(Path.GetDirectoryName(_path)!, ".installation.key"), 32, 64);
    }

    private static void RequirePolicyRole()
    {
        if (typeof(T) != typeof(ApplicationBootstrap)) throw new InvalidOperationException("Protected policy slots belong to the Application role.");
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closing = true;
            return _mutations.CloseAsync();
        }
    }
}
