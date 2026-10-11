using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Recovery;

internal static class ConfiguredSiteRestoreCommand
{
    public static bool Recognizes(string[] args) => args.Length > 0 && string.Equals(args[0], "--site-restore-configured", StringComparison.Ordinal);

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 2) throw new ArgumentException("Use --site-restore-configured with one private absolute request file.", nameof(args));
        var request = BootstrapFile.Parse<ConfiguredSiteRestoreRequest>(PrivateOwnerFile.Read(args[1], 1, 1024 * 1024));
        var result = await ConfiguredSiteRestore.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(BootstrapFile.Serialize(result));
    }
}
