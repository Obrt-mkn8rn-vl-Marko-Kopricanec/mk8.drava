using System.Text;
using System.Security.Cryptography;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mk8.Drava.Application.Recovery;

// Preparation never starts listeners, generates service credentials, restores leases or performs DNS/ACME writes.
// Each new role receives its own provider-managed installation protection key.
internal static partial class ConfiguredSiteRestore
{
    public static async ValueTask<ConfiguredSiteRestoreResult> PrepareAsync(ConfiguredSiteRestoreRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var request = BootstrapFile.Parse<ConfiguredSiteRestoreRequest>(Encoding.UTF8.GetBytes(BootstrapFile.Serialize(input)));
        var placements = Validate(request);
        PrivateOwnerFile.RequirePrivateDirectory(Path.GetDirectoryName(request.DestinationDirectory) ?? throw new InvalidDataException("Missing restore parent."));
        using var workspace = PrivateSiteWorkspace.Create(request.DestinationDirectory);
        var recoveredPath = Path.Combine(workspace.StagingDirectory, "recovered");
        var result = await SiteRecoveryBundle.RestoreVersionedAsync(request.SourceDirectory, recoveredPath, request.Application.SiteId,
            request.ExpectedManifestSha256, request.MaximumRegistryBytes, request.MaximumFileBytes,
            request.ApprovedFiles, request.AllowCredentialEscrow, request.SchemaVersion, cancellationToken).ConfigureAwait(false);
        if (result.Receipt.SchemaVersion != request.SchemaVersion)
            throw new InvalidDataException("Configured restore request and approved recovery schema differ.");
        Directory.Move(Path.Combine(recoveredPath, "registry"), Path.Combine(workspace.StagingDirectory, "application"));
        PortableRuntimeDocuments? documents = null;
        if (request.SchemaVersion >= 2)
        {
            var policyDirectory = Path.Combine(workspace.StagingDirectory, "application", "policy");
            documents = request.SchemaVersion == 3
                ? await PortablePolicyEscrow.LoadRuntimeAsync(policyDirectory, request.Application.SiteId, request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false)
                : new PortableRuntimeDocuments(await PortablePolicyEscrow.LoadAsync(policyDirectory, request.Application.SiteId, request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false), PortableManualConfigurationDocument.Empty);
            Directory.Delete(policyDirectory, recursive: true);
        }
        workspace.CreateDirectory("gateway");
        var directories = new HashSet<string>(StringComparer.Ordinal) { workspace.StagingDirectory, Path.Combine(workspace.StagingDirectory, "application"), Path.Combine(workspace.StagingDirectory, "gateway") };
        foreach (var file in result.Receipt.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = placements[file.Name];
            var target = Path.Combine(workspace.StagingDirectory, relative);
            CreateParents(workspace.StagingDirectory, Path.GetDirectoryName(target) ?? throw new InvalidDataException("Missing material parent."), directories);
            File.Move(Path.Combine(recoveredPath, "files", file.Name), target, overwrite: false);
        }
        Directory.Delete(recoveredPath, recursive: true);
        var manual = request.SchemaVersion == 3 ? documents?.ManualConfiguration : null;
        var legacyManual = ImportApprovedManual(workspace, request);
        if (legacyManual is not null)
        {
            if (request.SchemaVersion == 3) throw new InvalidDataException("Restore mixes protected manual inputs with legacy configuration placements.");
            manual = legacyManual;
        }
        var prepared = await WriteRoleDocumentsAsync(workspace, request, result, documents, manual, directories, cancellationToken).ConfigureAwait(false);
        foreach (var directory in directories.OrderByDescending(static path => path.Length)) PrivateOwnerFile.SynchronizeDirectory(directory);
        cancellationToken.ThrowIfCancellationRequested();
        workspace.Publish();
        try { PrivateOwnerFile.SynchronizeDirectory(Path.GetDirectoryName(request.DestinationDirectory) ?? throw new InvalidDataException("Missing restore parent.")); }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            throw new IOException("Configured restore was published but synchronization failed; inspect before retrying.", exception);
        }
        return prepared;
    }

    private static async ValueTask<ConfiguredSiteRestoreResult> WriteRoleDocumentsAsync(PrivateSiteWorkspace workspace,
        ConfiguredSiteRestoreRequest request, SiteRecoveryResult result, PortableRuntimeDocuments? documents,
        PortableManualConfigurationDocument? manual, HashSet<string> directories, CancellationToken cancellationToken)
    {
        const string applicationDocument = "roles/application/Environment/.env";
        const string gatewayDocument = "roles/gateway/Environment/.env";
        directories.Add(workspace.CreateDirectory("roles"));
        directories.Add(workspace.CreateDirectory(Path.Combine("roles", "application")));
        directories.Add(workspace.CreateDirectory(Path.Combine("roles", "gateway")));
        var applicationEnvironment = workspace.CreateDirectory(Path.Combine("roles", "application", "Environment"));
        var gatewayEnvironment = workspace.CreateDirectory(Path.Combine("roles", "gateway", "Environment"));
        directories.Add(applicationEnvironment);
        directories.Add(gatewayEnvironment);
        var applicationSettings = new List<Supprocom.Secrets.SupprocomSecretSetting>
        {
            new("Drava:ApplicationBootstrap", BootstrapFile.Serialize(request.Application)),
        };
        if (manual is not null)
        {
            applicationSettings.Add(new Supprocom.Secrets.SupprocomSecretSetting(
            PortableManualConfigurationSource.CollectionKey, manual.Reveal()));
        }

        await workspace.WriteAsync(applicationDocument, Encoding.UTF8.GetBytes(Supprocom.Secrets.SupprocomSecretDocument.Serialize(applicationSettings)), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync(gatewayDocument, Encoding.UTF8.GetBytes(PortableConfiguration.Serialize(request.Gateway)), cancellationToken).ConfigureAwait(false);
        // Protect before recording bytes. Loading the published role must not change its receipt.
        var applicationPath = Path.Combine(workspace.StagingDirectory, applicationDocument);
        var gatewayPath = Path.Combine(workspace.StagingDirectory, gatewayDocument);
        var registryReceipt = result.Receipt.Registry;
        var applicationRole = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(applicationPath, cancellationToken).ConfigureAwait(false);
        await using (applicationRole.ConfigureAwait(false))
        {
            if (documents is not null)
            {
                var references = result.Receipt.Registry.PolicyEscrow?.References ?? throw new InvalidDataException("Recovered policy reference inventory is absent.");
                var payloads = new PortablePolicyPayloadStore(applicationRole);
                await payloads.RestoreAsync(documents.PolicyArchive, references, cancellationToken).ConfigureAwait(false);
                registryReceipt = await RegistrySnapshot.ValidatePreparedProtectedAsync(Path.Combine(workspace.StagingDirectory, "application"),
                    request.Application.SiteId, request.MaximumRegistryBytes,
                    await payloads.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            if (manual is not null) await ValidateManualRoleAsync(applicationRole, cancellationToken).ConfigureAwait(false);
        }
        _ = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(gatewayPath, cancellationToken).ConfigureAwait(false);
        var applicationBytes = await File.ReadAllBytesAsync(applicationPath, cancellationToken).ConfigureAwait(false);
        var gatewayBytes = await File.ReadAllBytesAsync(gatewayPath, cancellationToken).ConfigureAwait(false);
        var receipt = new ConfiguredSiteRestoreReceipt(request.SchemaVersion, request.ExpectedManifestSha256, registryReceipt,
            result.Receipt.Files, request.Placements, Convert.ToHexString(SHA256.HashData(applicationBytes)), Convert.ToHexString(SHA256.HashData(gatewayBytes)))
        { PolicyReferences = result.Receipt.Registry.PolicyEscrow?.References, ManualConfiguration = manual is null ? null : DescribeManualImport(manual) };
        var receiptBytes = Encoding.UTF8.GetBytes(BootstrapFile.Serialize(receipt));
        await workspace.WriteAsync("recovery.json", receiptBytes, cancellationToken).ConfigureAwait(false);
        foreach (var name in new[] { applicationDocument, gatewayDocument, "roles/application/Environment/.installation.key", "roles/gateway/Environment/.installation.key", "recovery.json" })
        {
            var file = PrivateOwnerFile.AcquireLease(Path.Combine(workspace.StagingDirectory, name));
            await using (file.ConfigureAwait(false)) { RandomAccess.FlushToDisk(file.SafeFileHandle); }
        }
        return new ConfiguredSiteRestoreResult(receipt, Convert.ToHexString(SHA256.HashData(receiptBytes)));
    }

    private static async Task ValidateManualRoleAsync(PortableRole<ApplicationBootstrap> role, CancellationToken cancellationToken)
    {
        var paths = new Mk8.Drava.Application.DAL.Configuration.Paths.ApplicationDataDirectoryProvider(role.Configuration.StateDirectory);
        var loader = new ProxyConfigurationLoader(paths, new ProxyDataDirectoryBootstrapper(paths), new SiteConfigurationParser(),
            new ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new ProxyUrlSyntaxPolicy(),
            new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System,
            new PortableUpstreamTrustMaterialSource(role.Configuration), new PortableManualConfigurationSource(role));
        var result = await loader.ValidateAsync(cancellationToken).ConfigureAwait(false);
        if (result is not ProxyConfigurationLoadResult.ValidatedResult)
            throw new InvalidDataException("Recovered protected manual inputs fail current startup validation.");
    }

    private static Dictionary<string, string> Validate(ConfiguredSiteRestoreRequest request)
    {
        if (request.SchemaVersion is not (1 or 2 or 3) || (request.SchemaVersion >= 2 && !request.AllowCredentialEscrow) || !Path.IsPathFullyQualified(request.DestinationDirectory) ||
            !string.Equals(Path.GetFullPath(request.DestinationDirectory), request.DestinationDirectory, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Configured restore requires an approved supported schema and a canonical new destination.");
        }
        request.Application.Validate(); request.Gateway.Validate();
        var application = request.Application;
        var gateway = request.Gateway;
        if (!string.Equals(application.SiteId, gateway.SiteId, StringComparison.Ordinal) || !string.Equals(application.GatewayId, gateway.GatewayId, StringComparison.Ordinal) ||
            !string.Equals(application.StateDirectory, Path.Combine(request.DestinationDirectory, "application"), StringComparison.Ordinal) ||
            !string.Equals(gateway.StateDirectory, Path.Combine(request.DestinationDirectory, "gateway"), StringComparison.Ordinal) ||
            !string.Equals(application.IngressAddress, gateway.BindAddress, StringComparison.Ordinal) || application.HttpPort != gateway.HttpPort ||
            application.HttpsPort != gateway.HttpsPort || application.ManagementPort != gateway.ManagementPort)
        {
            throw new InvalidDataException("Restored Application and Gateway identities, state paths and presentation must agree.");
        }
        if (application.Listen.UnixSocketPath.Length == 0 || !string.Equals(Path.GetDirectoryName(application.Listen.UnixSocketPath), application.StateDirectory, StringComparison.Ordinal) ||
            application.Listen != gateway.Application)
        {
            throw new NotSupportedException("Configured restore requires matched local Unix IPC supported by the current Application host.");
        }
        if (application.Controller is { } controller && (controller.RegistrationPort != gateway.RegistrationPort ||
            !string.Equals(controller.EnrollmentRootFingerprint, gateway.EnrollmentRootFingerprint, StringComparison.Ordinal) || controller.ServingTrust != gateway.ServingTrust))
        {
            throw new InvalidDataException("Restored controller and Gateway enrollment/serving policy must agree.");
        }
        if (!application.IpcToken.Equals(gateway.ApplicationToken))
            throw new InvalidDataException("Restored private IPC credentials must agree.");
        var placements = ValidatePlacements(request);
        if (application.Controller is { } owner) RequireControllerMaterial(owner, request, placements);
        return placements;
    }

    private static Dictionary<string, string> ValidatePlacements(ConfiguredSiteRestoreRequest request)
    {
        if (request.Placements.Length > 128 || (request.Placements.Length == 0 && request.SchemaVersion == 1) || request.Placements.Length != request.ApprovedFiles.Length ||
            request.ApprovedFiles.Any(static file => file is null))
        {
            throw new InvalidDataException("Configured restore requires one placement per approved file.");
        }
        var placements = new Dictionary<string, string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in request.Placements)
        {
            if (placement is null || !request.ApprovedFiles.Any(file => string.Equals(file.Name, placement.Name, StringComparison.Ordinal)) ||
                !placements.TryAdd(placement.Name, placement.RelativePath) || !targets.Add(placement.RelativePath))
            {
                throw new InvalidDataException("Restore placement is missing, duplicated or unapproved.");
            }
            var relative = placement.RelativePath;
            if (relative.Length is < 1 or > 512 || relative.Contains('\\', StringComparison.Ordinal) || relative.Contains('\0', StringComparison.Ordinal) || Path.IsPathRooted(relative) ||
                relative.Split('/').Any(static part => part.Length == 0 || part is "." or ".."))
            {
                throw new InvalidDataException("Restore placement requires a canonical portable relative path.");
            }
            var credential = request.ApprovedFiles.First(file => string.Equals(file.Name, placement.Name, StringComparison.Ordinal)).Credential;
            var material = string.Equals(relative, "material/" + placement.Name, StringComparison.Ordinal);
            var configuration = !credential && (string.Equals(relative, "application/config/proxy.json", StringComparison.Ordinal) || relative.StartsWith("application/config/sites/", StringComparison.Ordinal));
            if (!material && !configuration) throw new InvalidDataException("Only explicit private material and Application configuration may be restored; runtime/cache/coordination files are excluded.");
        }
        return placements;
    }

    private static void RequireMaterial(string path, bool credential, ConfiguredSiteRestoreRequest request, Dictionary<string, string> placements)
    {
        if (path.Length == 0) return;
        foreach (var file in request.ApprovedFiles)
        {
            if (file.Credential == credential && placements.TryGetValue(file.Name, out var relative) &&
                string.Equals(path, Path.Combine(request.DestinationDirectory, relative), StringComparison.Ordinal))
            {
                return;
            }
        }
        throw new InvalidDataException("Restored bootstrap references material outside the explicitly approved inventory.");
    }

    private static void RequireControllerMaterial(ControllerBootstrap controller, ConfiguredSiteRestoreRequest request, Dictionary<string, string> placements)
    {
        if (!controller.Acme.Enabled) return;
        RequireMaterial(controller.Acme.CleanupJournalPath, credential: true, request, placements);
    }

    private static void CreateParents(string staging, string directory, HashSet<string> directories)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Configured restore requires the qualified Linux private filesystem profile.");
        var parents = new Stack<string>();
        for (var current = new DirectoryInfo(directory); current is not null && !string.Equals(current.FullName, staging, StringComparison.Ordinal); current = current.Parent)
        {
            if (!current.FullName.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Restore directory escapes its private staging root.");
            parents.Push(current.FullName);
        }
        while (parents.TryPop(out var parent))
        {
            Directory.CreateDirectory(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            PrivateOwnerFile.RequirePrivateDirectory(parent);
            directories.Add(parent);
        }
    }
}
