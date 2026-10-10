using System.Net;
using System.Net.Sockets;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class StartupSmokeTests
{
    public static async Task StartsFromFreshDataDirectoryAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var configDirectory = Path.Combine(temp.Path, "config");
        var sitesDirectory = Path.Combine(configDirectory, "sites");
        AssertEx.False(Directory.Exists(configDirectory));
        AssertEx.False(Directory.Exists(sitesDirectory));
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
        var store = host.Services.GetRequiredService<IProxyConfigurationStore>();
        var snapshot = store.Snapshot;
        var runtimeState = host.Services.GetRequiredService<ProxyRuntimeState>();
        var runtime = await WaitForRuntimeAsync(runtimeState, static snapshot => string.Equals(snapshot.LastError, "No configured proxy listener.", StringComparison.Ordinal), CancellationToken.None).ConfigureAwait(false);
        AssertEx.True(Directory.Exists(configDirectory));
        AssertEx.True(Directory.Exists(sitesDirectory));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "logs")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "certs")));
        AssertEx.True(Directory.Exists(Path.Combine(temp.Path, "state")));
        AssertEx.True(File.Exists(Path.Combine(configDirectory, "proxy.json")));
        AssertEx.True(File.Exists(Path.Combine(sitesDirectory, "example.site.yaml")));
        AssertEx.Equal(0, snapshot.Listeners.Count);
        AssertEx.Equal(0, snapshot.Routes.Count);
        AssertEx.Equal(TimeSpan.FromSeconds(10), snapshot.Timeouts.ClientRequestHeadTimeout);
        AssertEx.False(runtime.IsRunning);
        AssertEx.Equal("No configured proxy listener.", runtime.LastError);
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public static async Task FailsStartupWhenExistingSiteConfigIsInvalidAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var sites = Directory.CreateDirectory(Path.Combine(temp.Path, "config", "sites")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sites, "broken.json"), "{ nope").ConfigureAwait(false);
        using var host = BuildProxyHost(temp.Path);
        try
        {
            await AssertEx.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken.None)).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public static async Task StartsWithValidSiteConfigAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpPort();
        ConfigurationTests.WriteSite(temp.Path, "home.json", proxyPort, upstreamPort: GetFreeTcpPort());
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
        var store = host.Services.GetRequiredService<IProxyConfigurationStore>();
        var runtimeState = host.Services.GetRequiredService<ProxyRuntimeState>();
        var runtime = await WaitForRuntimeAsync(runtimeState, static snapshot => snapshot.IsRunning, CancellationToken.None).ConfigureAwait(false);
        AssertEx.Equal(1, store.Snapshot.Listeners.Count);
        AssertEx.Equal(1, store.Snapshot.Routes.Count);
        AssertEx.True(runtime.IsRunning);
        AssertEx.Equal(proxyPort.ToString(System.Globalization.CultureInfo.InvariantCulture), runtime.Endpoint?.Split(':').Last());
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static IHost BuildProxyHost(string dataDirectory)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [$"{MdravaDataDirectoryOptions.SectionName}:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
        {
            services.AddProxyDataPlane(context.Configuration);
        }).Build();
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<ProxyRuntimeSnapshot> WaitForRuntimeAsync(ProxyRuntimeState runtimeState, Func<ProxyRuntimeSnapshot, bool> predicate, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (true)
        {
            var snapshot = runtimeState.Snapshot();
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-startup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
