using System.Security.Cryptography;
using System.Text;
using Supprocom.Secrets;

namespace Mk8.Drava.Configuration;

// Explicit backup inputs only. The export contains policy data and a new provider key, not service credentials.
public static class PortablePolicyEscrow
{
    private const string MetadataKey = "Drava:PolicyEscrow:Metadata";
    private const string ArchiveKey = "Drava:PolicyEscrow:Archive";

    public static async Task<PortablePolicyArchive> ReadApplicationArchiveAsync(string collectionPath, string siteId,
        string stateDirectory, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        if (!allowCredentialEscrow) throw new InvalidOperationException("Policy escrow requires explicit credential approval.");
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        RequireSite(siteId);
        var store = RequireProtectedStore(collectionPath, cancellationToken);
        if (await store.GetStateAsync(cancellationToken).ConfigureAwait(false) != SecretFileProtectionState.Protected)
            throw new InvalidDataException("Policy escrow refuses an unprotected source collection.");
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(collectionPath, cancellationToken).ConfigureAwait(false);
        await using var roleLifetime = role.ConfigureAwait(false);
        if (!string.Equals(role.Configuration.SiteId, siteId, StringComparison.Ordinal)
            || !string.Equals(role.Configuration.StateDirectory, stateDirectory, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Policy escrow source differs from the caller's stopped Application owner.");
        }

        return role.ReadPolicyDocuments().Archive;
    }

    public static async Task ExportAsync(string environmentDirectory, string siteId, PortablePolicyArchive archive,
        bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        if (!allowCredentialEscrow) throw new InvalidOperationException("Policy escrow requires explicit credential approval.");
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentDirectory);
        ArgumentNullException.ThrowIfNull(archive);
        RequireSite(siteId);
        RequireNewEnvironment(environmentDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(environmentDirectory, ".env");
        var key = Path.Combine(environmentDirectory, ".installation.key");
        var document = SupprocomSecretDocument.Serialize([
            new SupprocomSecretSetting(MetadataKey, BootstrapFile.Serialize(new EscrowMetadata { SiteId = siteId })),
            new SupprocomSecretSetting(ArchiveKey, archive.Reveal()),
        ]);
        var bytes = Encoding.UTF8.GetBytes(document);
        var complete = false;
        FileStream? ownedFile = null;
        try
        {
            if (bytes.Length > InstallationCollectionBounds.MaximumDocumentBytes - 29)
                throw new InvalidDataException("Policy escrow exceeds its protected document bound.");
            ownedFile = OpenNew(path);
            await using (ownedFile.ConfigureAwait(false))
            {
                await ownedFile.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await ownedFile.FlushAsync(cancellationToken).ConfigureAwait(false);
                RandomAccess.FlushToDisk(ownedFile.SafeFileHandle);
            }
            var store = CreateStore(path, cancellationToken);
            await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (await store.GetStateAsync(CancellationToken.None).ConfigureAwait(false) != SecretFileProtectionState.Protected)
                throw new InvalidDataException("Policy escrow protection did not complete.");
            InstallationDocumentGuard.Synchronize(path, 1, InstallationCollectionBounds.MaximumDocumentBytes);
            InstallationDocumentGuard.Synchronize(key, 32, 64);
            complete = true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (!complete && ownedFile is not null) { File.Delete(path); File.Delete(key); }
        }
    }

    public static async Task<PortablePolicyArchive> LoadAsync(string environmentDirectory, string siteId,
        bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        if (!allowCredentialEscrow) throw new InvalidOperationException("Policy escrow requires explicit credential approval.");
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentDirectory);
        RequireSite(siteId);
        var path = Path.Combine(environmentDirectory, ".env");
        var store = RequireProtectedStore(path, cancellationToken);
        if (await store.GetStateAsync(cancellationToken).ConfigureAwait(false) != SecretFileProtectionState.Protected)
            throw new InvalidDataException("Policy escrow refuses an unprotected archive.");
        var snapshot = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        InstallationDocumentGuard.Require(path, 1, InstallationCollectionBounds.MaximumDocumentBytes);
        if (snapshot.Count != 2 || !snapshot.TryGetValue(MetadataKey, out var metadata) || !snapshot.TryGetValue(ArchiveKey, out var archive))
            throw new InvalidDataException("Policy escrow contains missing or unexpected material.");
        var envelope = BootstrapFile.Parse<EscrowMetadata>(Encoding.UTF8.GetBytes(metadata));
        if (envelope.SchemaVersion != 1 || !string.Equals(envelope.SiteId, siteId, StringComparison.Ordinal))
            throw new InvalidDataException("Policy escrow schema or site identity differs from the caller.");
        return new PortablePolicyArchive(archive);
    }

    private static FileStream OpenNew(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };
        if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private static SupprocomSecretFileStore RequireProtectedStore(string path, CancellationToken cancellationToken)
    {
        InstallationDocumentGuard.RequireDurabilitySupport();
        InstallationDocumentGuard.Require(path, 1, InstallationCollectionBounds.MaximumDocumentBytes);
        InstallationDocumentGuard.Require(Path.Combine(Path.GetDirectoryName(path) ?? throw new InvalidDataException("Policy collection parent is missing."), ".installation.key"), 32, 64);
        return CreateStore(path, cancellationToken);
    }

    private static SupprocomSecretFileStore CreateStore(string path, CancellationToken cancellationToken)
    {
        var options = new SupprocomSecretsOptions
        {
            EnvironmentName = "Production", FileOverridesProcessEnvironment = true, StartupCancellationToken = cancellationToken,
        };
        options.File.Directory = Path.GetDirectoryName(path);
        options.File.ActiveName = Path.GetFileName(path);
        options.File.Import = SecretFileImport.None;
        options.File.Recovery = SecretFileRecovery.None;
        options.File.Protection = SecretFileProtection.InstallationBoundAesGcm;
        return new SupprocomSecretFileStore(options);
    }

    private static void RequireNewEnvironment(string directory)
    {
        InstallationDocumentGuard.RequireDurabilitySupport();
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        InstallationDocumentGuard.RequirePrivateDirectory(directory);
        if (!Path.IsPathFullyQualified(directory) || !string.Equals(Path.GetFullPath(directory), directory, StringComparison.Ordinal)
            || !Directory.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null || Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new InvalidDataException("Policy export requires a new private empty environment directory.");
        }
    }

    private static void RequireSite(string siteId)
    {
        ArgumentNullException.ThrowIfNull(siteId);
        if (siteId.Length is < 1 or > 128) throw new InvalidDataException("Policy escrow requires a bounded site identity.");
    }

    private sealed record EscrowMetadata
    {
        public int SchemaVersion { get; init; } = 1;
        public string SiteId { get; init; } = "";
    }
}
