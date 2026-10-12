using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;

namespace Mk8.Drava.CompatibilityTests;

// Owned development fixtures only. This retains the original native composition;
// it is not linked into either production host or any published SDK.
internal static class DevelopmentNativeReference
{
    public static async Task RunAsync(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        using var host = Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Mdrava:DataDirectory"] = Path.GetFullPath(dataDirectory),
            });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
        {
            services.AddProxyDataPlane(context.Configuration);
        }).Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
