using System.Text.Json;
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
        if (request.SchemaVersion != 1 || (!restoring && request.ExpectedImageSha256.Length != 0))
            throw new InvalidDataException("Unsupported or mixed registry recovery request.");
        var receipt = restoring
            ? await RegistrySnapshot.RestoreAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes, request.ExpectedImageSha256, cancellationToken).ConfigureAwait(false)
            : await RegistrySnapshot.CreateAsync(request.SourceDirectory, request.DestinationDirectory, request.SiteId, request.MaximumBytes, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(receipt));
    }
}
