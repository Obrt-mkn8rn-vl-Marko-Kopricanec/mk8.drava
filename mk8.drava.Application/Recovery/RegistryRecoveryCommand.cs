using System.Text.Json;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Recovery;

internal static class RegistryRecoveryCommand
{
    public static bool Recognizes(string[] args) => args.Length > 0 && args[0] is "--registry-backup" or "--registry-restore";

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var restoring = string.Equals(args[0], "--registry-restore", StringComparison.Ordinal);
        if (args.Length != 1) throw new ArgumentException("Registry recovery settings come only from the installation-local Supprocom collection.", nameof(args));
        var request = await PortableConfiguration.LoadAsync<RegistryRecoveryRequest>(cancellationToken).ConfigureAwait(false);
        if (request.SchemaVersion is not (1 or 2 or 3) || (!restoring && request.ExpectedImageSha256.Length != 0))
            throw new InvalidDataException("Unsupported or mixed registry recovery request.");
        if (request.SchemaVersion == 1 && (request.ApplicationCollectionPath.Length != 0 || request.AllowCredentialEscrow || request.ExpectedProtectedImage is not null))
            throw new InvalidDataException("Legacy registry recovery cannot include policy escrow inputs.");
        RegistrySnapshotReceipt receipt;
        if (request.SchemaVersion >= 2)
        {
            if (!request.AllowCredentialEscrow || request.ExpectedImageSha256.Length != 0 ||
                (restoring ? request.ApplicationCollectionPath.Length != 0 || request.ExpectedProtectedImage is null : request.ApplicationCollectionPath.Length == 0 || request.ExpectedProtectedImage is not null))
            {
                throw new InvalidDataException("Protected registry recovery requires explicit, operation-specific escrow inputs.");
            }

            receipt = request.SchemaVersion == 3
                ? restoring
                ? await RegistrySnapshot.RestoreRuntimeAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes,
                    request.ExpectedProtectedImage ?? throw new InvalidDataException("Approved runtime receipt is absent."), request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false)
                : await RegistrySnapshot.CreateRuntimeAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes,
                    request.ApplicationCollectionPath, request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false)
                : restoring
                ? await RegistrySnapshot.RestoreProtectedAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes,
                    request.ExpectedProtectedImage ?? throw new InvalidDataException("Approved registry receipt is absent."), request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false)
                : await RegistrySnapshot.CreateProtectedAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes,
                    request.ApplicationCollectionPath, request.AllowCredentialEscrow, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            receipt = restoring
                ? await RegistrySnapshot.RestoreAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes, request.ExpectedImageSha256, cancellationToken).ConfigureAwait(false)
                : await RegistrySnapshot.CreateAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes, cancellationToken).ConfigureAwait(false);
        }
        Console.WriteLine(JsonSerializer.Serialize(receipt));
    }
}
