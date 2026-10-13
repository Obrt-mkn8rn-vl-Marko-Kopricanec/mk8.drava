using Supprocom.Secrets;

namespace Mk8.Drava.Configuration;

/// <summary>Installation-local configuration, without ambient or legacy fallback.</summary>
public static class PortableConfiguration
{
    public static string StartupPath(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != 0) throw new ArgumentException("Service configuration is loaded only from the installation-local Supprocom collection.", nameof(args));
        return Path.Combine(AppContext.BaseDirectory, "Environment", ".env");
    }

    public static Task<T> LoadAsync<T>(CancellationToken cancellationToken) where T : class =>
        LoadDocumentAsync<T>(StartupPath([]), cancellationToken);

    public static Task<PortableRole<T>> LoadRoleAsync<T>(CancellationToken cancellationToken) where T : class =>
        LoadRoleDocumentAsync<T>(StartupPath([]), cancellationToken);

    // Test fixtures own independent disposable output roots. Production callers use fixed startup paths.
    internal static async Task<T> LoadDocumentAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        var role = await LoadRoleDocumentAsync<T>(path, cancellationToken).ConfigureAwait(false);
        await using var roleLifetime = role.ConfigureAwait(false);
        return role.Configuration;
    }

    internal static async Task<PortableRole<T>> LoadRoleDocumentAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Portable configuration requires an absolute location.");
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length is < 1 or > InstallationCollectionBounds.MaximumDocumentBytes)
            throw new InvalidDataException("Portable configuration must be an existing bounded regular document.");
        InstallationDocumentGuard.Require(path, 1, InstallationCollectionBounds.MaximumDocumentBytes);
        var keyPath = Path.Combine(file.DirectoryName!, ".installation.key");
        if (File.Exists(keyPath) || Directory.Exists(keyPath) || new FileInfo(keyPath).LinkTarget is not null) InstallationDocumentGuard.Require(keyPath, 32, 64);
        var options = new SupprocomSecretsOptions
        {
            EnvironmentName = "Production",
            FileOverridesProcessEnvironment = true,
            StartupCancellationToken = cancellationToken,
        };
        options.File.Directory = file.DirectoryName;
        options.File.ActiveName = file.Name;
        options.File.Import = SecretFileImport.None;
        options.File.Recovery = SecretFileRecovery.None;
        options.File.Protection = SecretFileProtection.InstallationBoundAesGcm;
        var store = new SupprocomSecretFileStore(options);
        var snapshot = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        InstallationDocumentGuard.Require(path, 1, InstallationCollectionBounds.MaximumDocumentBytes);
        InstallationDocumentGuard.Require(keyPath, 32, 64);
        InstallationCollectionBounds.RequireSettings([.. snapshot.Select(static setting => new SupprocomSecretSetting(setting.Key, setting.Value))]);
        if (!snapshot.TryGetValue(Key<T>(), out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The portable collection does not contain the requested role configuration.");
        cancellationToken.ThrowIfCancellationRequested();
        return new PortableRole<T>(BootstrapFile.Parse<T>(System.Text.Encoding.UTF8.GetBytes(value)), store, path, value, snapshot);
    }

    public static string Serialize<T>(T value) => SerializeDocument<T>(BootstrapFile.Serialize(value));

    public static string SerializeDocument<T>(string document) => SupprocomSecretDocument.Serialize(
        [new SupprocomSecretSetting(Key<T>(), document)]);

    internal static string Key<T>() => "Drava:" + typeof(T).Name;
}
