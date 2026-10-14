using System.Text.Json;
using System.Text.Json.Serialization;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Recovery;

internal static class SiteRecoveryCommand
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
    };

    public static bool Recognizes(string[] args) => args.Length > 0 && args[0] is "--site-backup" or "--site-restore";

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 1) throw new ArgumentException("Site recovery settings come only from the installation-local Supprocom collection.", nameof(args));
        var request = await PortableConfiguration.LoadAsync<SiteRecoveryRequest>(cancellationToken).ConfigureAwait(false);
        if (request.SchemaVersion is not (1 or 2)) throw new InvalidDataException("Unsupported site recovery request schema.");
        var restoring = string.Equals(args[0], "--site-restore", StringComparison.Ordinal);
        if (restoring ? request.Files.Length != 0 || request.GatewayDirectory.Length != 0 : request.ApprovedFiles.Length != 0 || request.ExpectedManifestSha256.Length != 0)
            throw new InvalidDataException("Recovery request mixes backup and restore inputs.");
        if (request.SchemaVersion == 1 ? request.ApplicationCollectionPath.Length != 0 : !request.AllowCredentialEscrow ||
            (restoring ? request.ApplicationCollectionPath.Length != 0 : request.ApplicationCollectionPath.Length == 0))
        {
            throw new InvalidDataException("Recovery schema and operation-specific policy escrow inputs differ.");
        }

        var result = restoring
            ? await SiteRecoveryBundle.RestoreVersionedAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId,
                request.ExpectedManifestSha256, request.MaximumRegistryBytes, request.MaximumFileBytes,
                request.ApprovedFiles, request.AllowCredentialEscrow, request.SchemaVersion, cancellationToken).ConfigureAwait(false)
            : request.SchemaVersion == 2
            ? await SiteRecoveryBundle.CreateProtectedAsync(request.SourceDirectory, request.GatewayDirectory, request.DestinationDirectory,
                request.SiteId, request.MaximumRegistryBytes, request.MaximumFileBytes, request.Files,
                request.ApplicationCollectionPath, request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false)
            : await SiteRecoveryBundle.CreateAsync(request.SourceDirectory, request.GatewayDirectory, request.DestinationDirectory,
                request.SiteId, request.MaximumRegistryBytes, request.MaximumFileBytes, request.Files,
                request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false);
        if (result.Receipt.SchemaVersion != request.SchemaVersion) throw new InvalidDataException("Requested and recovered site schema differ.");
        Console.WriteLine(JsonSerializer.Serialize(result, Options));
    }
}
