using Supprocom.Secrets;

namespace Mk8.Drava.Configuration;

// Configuration remains the startup snapshot. Only this role's protected material slot is writable.
public sealed class PortableRole<T> : IAsyncDisposable where T : class
{
    private readonly ISecretDocumentUpdater _store;
    private readonly string _path;
    private readonly string _configurationDocument;
    private readonly Lock _gate = new();
    private string? _encodedMaterial;
    private SecretBytes _material;
    private bool _closing;
    private Task? _writeTask;
    public T Configuration { get; }
    private static string MaterialKey => "Drava:Material:" + typeof(T).Name;

    internal PortableRole(T configuration, ISecretDocumentUpdater store, string path,
        string configurationDocument, IReadOnlyDictionary<string, string> snapshot)
    {
        Configuration = configuration;
        _store = store;
        _path = path;
        _configurationDocument = configurationDocument;
        snapshot.TryGetValue(MaterialKey, out _encodedMaterial);
        _material = _encodedMaterial is null ? SecretBytes.Empty
            : BootstrapFile.Parse<SecretBytes>(System.Text.Encoding.UTF8.GetBytes(_encodedMaterial));
        if (_material.Length > (128 * 1024) + 36) throw new InvalidDataException("Protected presentation material exceeds its slot bound.");
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
            _writeTask = WriteCoreAsync(material, cancellationToken);
            return new ValueTask(_writeTask);
        }
    }

    private async Task WriteCoreAsync(SecretBytes material, CancellationToken cancellationToken)
    {
        RequireDocuments();
        var encoded = BootstrapFile.Serialize(material);
        await _store.UpdateDocumentAsync(settings => Transform(settings, encoded), cancellationToken).ConfigureAwait(false);
        RequireDocuments();
        lock (_gate) { _encodedMaterial = encoded; _material = material; }
    }

    private List<SupprocomSecretSetting> Transform(IReadOnlyList<SupprocomSecretSetting> settings, string encoded)
    {
        string? role = null;
        string? prior = null;
        var replacement = new List<SupprocomSecretSetting>(settings.Count + 1);
        foreach (var setting in settings)
        {
            if (string.Equals(setting.Key, PortableConfiguration.Key<T>(), StringComparison.OrdinalIgnoreCase)) role = setting.Value;
            if (string.Equals(setting.Key, MaterialKey, StringComparison.OrdinalIgnoreCase)) prior = setting.Value;
            else replacement.Add(setting);
        }
        if (!string.Equals(role, _configurationDocument, StringComparison.Ordinal) || !string.Equals(prior, _encodedMaterial, StringComparison.Ordinal))
            throw new InvalidDataException("Protected role configuration or material changed outside the current startup owner.");
        replacement.Add(new SupprocomSecretSetting(MaterialKey, encoded));
        return replacement;
    }

    private void RequireDocuments()
    {
        InstallationDocumentGuard.Require(_path, 1, 4 * 1024 * 1024);
        InstallationDocumentGuard.Require(Path.Combine(Path.GetDirectoryName(_path)!, ".installation.key"), 32, 64);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closing = true;
            return _writeTask is null ? ValueTask.CompletedTask : new ValueTask(_writeTask);
        }
    }
}
