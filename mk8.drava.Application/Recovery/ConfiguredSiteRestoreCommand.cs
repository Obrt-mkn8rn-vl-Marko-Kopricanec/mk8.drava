using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Recovery;

internal static class ConfiguredSiteRestoreCommand
{
    public static bool Recognizes(string[] args) => args.Length > 0 && string.Equals(args[0], "--site-restore-configured", StringComparison.Ordinal);

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 1) throw new ArgumentException("Configured recovery settings come only from the installation-local Supprocom collection.", nameof(args));
        var request = await PortableConfiguration.LoadAsync<ConfiguredSiteRestoreRequest>(cancellationToken).ConfigureAwait(false);
        var result = await ConfiguredSiteRestore.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(BootstrapFile.Serialize(result));
    }
}
