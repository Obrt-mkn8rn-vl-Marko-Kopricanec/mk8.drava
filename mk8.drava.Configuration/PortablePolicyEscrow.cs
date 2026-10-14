using System.Security.Cryptography;
using System.Text;
using Supprocom.Secrets;

namespace Mk8.Drava.Configuration;

// Explicit backup inputs only. The export contains policy data and a new provider key, not service credentials.
public static class PortablePolicyEscrow
{
    private const string MetadataKey = "Drava:PolicyEscrow:Metadata";
    private const string ArchiveKey = "Drava:PolicyEscrow:Archive";
    private const string ManualKey = "Drava:PolicyEscrow:ManualSource";

    public static async Task<PortablePolicyArchive> ReadApplicationArchiveAsync(string collectionPath, string siteId,
        string stateDirectory, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        var documents = await ReadApplicationRuntimeAsync(collectionPath, siteId, stateDirectory, allowCredentialEscrow, cancellationToken).ConfigureAwait(false);
        if (!documents.ManualConfiguration.IsEmpty) throw new InvalidDataException("Policy-only backup cannot omit protected manual configuration; use runtime recovery.");
        return documents.PolicyArchive;
    }

    public static Task<PortableRuntimeDocuments> ReadApplicationRuntimeAsync(string collectionPath, string siteId,
        string stateDirectory, bool allowCredentialEscrow, CancellationToken cancellationToken) =>
        ProtectedConfigurationOperation.RunAsync(() => ReadApplicationRuntimeCoreAsync(collectionPath, siteId, stateDirectory, allowCredentialEscrow, cancellationToken));

    private static async Task<PortableRuntimeDocuments> ReadApplicationRuntimeCoreAsync(string collectionPath, string siteId,
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

        return new PortableRuntimeDocuments(role.ReadPolicyDocuments().Archive, role.ReadManualConfiguration());
    }

    public static Task ExportAsync(string environmentDirectory, string siteId, PortablePolicyArchive archive,
        bool allowCredentialEscrow, CancellationToken cancellationToken) =>
        ExportCoreAsync(environmentDirectory, siteId, archive, manual: null, allowCredentialEscrow, cancellationToken);

    public static Task ExportRuntimeAsync(string environmentDirectory, string siteId, PortableRuntimeDocuments documents,
        bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return ExportCoreAsync(environmentDirectory, siteId, documents.PolicyArchive, documents.ManualConfiguration, allowCredentialEscrow, cancellationToken);
    }

    private static Task ExportCoreAsync(string environmentDirectory, string siteId, PortablePolicyArchive archive,
        PortableManualConfigurationDocument? manual, bool allowCredentialEscrow, CancellationToken cancellationToken) =>
        ProtectedConfigurationOperation.RunAsync(() => ExportProtectedCoreAsync(environmentDirectory, siteId, archive, manual, allowCredentialEscrow, cancellationToken));

    private static async Task ExportProtectedCoreAsync(string environmentDirectory, string siteId, PortablePolicyArchive archive,
        PortableManualConfigurationDocument? manual, bool allowCredentialEscrow, CancellationToken cancellationToken)
    {
        if (!allowCredentialEscrow) throw new InvalidOperationException("Policy escrow requires explicit credential approval.");
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentDirectory);
        ArgumentNullException.ThrowIfNull(archive);
        RequireSite(siteId);
        RequireNewEnvironment(environmentDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(environmentDirectory, ".env");
        var key = Path.Combine(environmentDirectory, ".installation.key");
        var settings = new List<SupprocomSecretSetting>
        {
            new(MetadataKey, BootstrapFile.Serialize(new EscrowMetadata { SchemaVersion = manual is null ? 1 : 2, SiteId = siteId })),
            new(ArchiveKey, archive.Reveal()),
        };
        if (manual is not null)
        {
            settings.Add(new SupprocomSecretSetting(ManualKey, manual.Reveal()));
            // Use live-purpose keys for the existing partition quotas; the export key has a separate bound below.
            InstallationCollectionBounds.RequireSettings([
                new SupprocomSecretSetting(InstallationCollectionBounds.PolicyArchiveKey, archive.Reveal()),
                new SupprocomSecretSetting(InstallationCollectionBounds.ManualSourceKey, manual.Reveal()),
                settings[0],
            ]);
        }
        var document = SupprocomSecretDocument.Serialize(settings);
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
            var store = PortableConfiguration.CreateStore(path, cancellationToken);
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
        var documents = await LoadCoreAsync(environmentDirectory, siteId, includeManual: false, allowCredentialEscrow, cancellationToken).ConfigureAwait(false);
        return documents.PolicyArchive;
    }

    public static Task<PortableRuntimeDocuments> LoadRuntimeAsync(string environmentDirectory, string siteId,
        bool allowCredentialEscrow, CancellationToken cancellationToken) =>
        LoadCoreAsync(environmentDirectory, siteId, includeManual: true, allowCredentialEscrow, cancellationToken);

    private static Task<PortableRuntimeDocuments> LoadCoreAsync(string environmentDirectory, string siteId,
        bool includeManual, bool allowCredentialEscrow, CancellationToken cancellationToken) =>
        ProtectedConfigurationOperation.RunAsync(() => LoadProtectedCoreAsync(environmentDirectory, siteId, includeManual, allowCredentialEscrow, cancellationToken));

    private static async Task<PortableRuntimeDocuments> LoadProtectedCoreAsync(string environmentDirectory, string siteId,
        bool includeManual, bool allowCredentialEscrow, CancellationToken cancellationToken)
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
        if (snapshot.Count != (includeManual ? 3 : 2) || !snapshot.TryGetValue(MetadataKey, out var metadata) || !snapshot.TryGetValue(ArchiveKey, out var archive))
            throw new InvalidDataException("Policy escrow contains missing or unexpected material.");
        var envelope = BootstrapFile.Parse<EscrowMetadata>(Encoding.UTF8.GetBytes(metadata));
        if (envelope.SchemaVersion != (includeManual ? 2 : 1) || !string.Equals(envelope.SiteId, siteId, StringComparison.Ordinal))
            throw new InvalidDataException("Policy escrow schema or site identity differs from the caller.");
        var manual = PortableManualConfigurationDocument.Empty;
        if (includeManual)
        {
            if (!snapshot.TryGetValue(ManualKey, out var value)) throw new InvalidDataException("Runtime recovery is missing its protected manual configuration.");
            manual = new PortableManualConfigurationDocument(value);
            InstallationCollectionBounds.RequireSettings([
                new SupprocomSecretSetting(InstallationCollectionBounds.PolicyArchiveKey, archive),
                new SupprocomSecretSetting(InstallationCollectionBounds.ManualSourceKey, value),
                new SupprocomSecretSetting(MetadataKey, metadata),
            ]);
        }
        return new PortableRuntimeDocuments(new PortablePolicyArchive(archive), manual);
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
        return PortableConfiguration.CreateStore(path, cancellationToken);
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
