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

    // Test fixtures own independent disposable output roots. Production callers use LoadAsync.
    internal static async Task<T> LoadDocumentAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Portable configuration requires an absolute location.");
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length is < 1 or > 4 * 1024 * 1024)
            throw new InvalidDataException("Portable configuration must be an existing bounded regular document.");
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
        var snapshot = await new SupprocomSecretFileStore(options).LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.TryGetValue(Key<T>(), out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The portable collection does not contain the requested role configuration.");
        cancellationToken.ThrowIfCancellationRequested();
        return BootstrapFile.Parse<T>(System.Text.Encoding.UTF8.GetBytes(value));
    }

    public static string Serialize<T>(T value) => SerializeDocument<T>(BootstrapFile.Serialize(value));

    public static string SerializeDocument<T>(string document) => SupprocomSecretDocument.Serialize(
        [new SupprocomSecretSetting(Key<T>(), document)]);

    internal static string Key<T>() => "Drava:" + typeof(T).Name;
}
