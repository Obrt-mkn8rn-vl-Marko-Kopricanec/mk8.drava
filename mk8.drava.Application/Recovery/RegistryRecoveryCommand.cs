using System.Globalization;
using System.Text.Json;
using Mk8.Drava.Application.DAL.Registry;

namespace Mk8.Drava.Application.Recovery;

internal static class RegistryRecoveryCommand
{
    public static bool Recognizes(string[] args) => args.Length > 0 && args[0] is "--registry-backup" or "--registry-restore";

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var restoring = string.Equals(args[0], "--registry-restore", StringComparison.Ordinal);
        if (args.Length != (restoring ? 9 : 7) || !string.Equals(args[3], "--site", StringComparison.Ordinal) ||
            !string.Equals(args[5], "--max-bytes", StringComparison.Ordinal) ||
            !long.TryParse(args[6], NumberStyles.None, CultureInfo.InvariantCulture, out var maximumBytes) ||
            (restoring && !string.Equals(args[7], "--sha256", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Use --registry-backup <source> <new-destination> --site <identity> --max-bytes <bound>, or --registry-restore with an additional --sha256 <expected-image-digest>.", nameof(args));
        }
        var receipt = restoring
            ? await RegistrySnapshot.RestoreAsync(args[1], args[2], args[4], maximumBytes, args[8], cancellationToken).ConfigureAwait(false)
            : await RegistrySnapshot.CreateAsync(args[1], args[2], args[4], maximumBytes, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(receipt));
    }
}
