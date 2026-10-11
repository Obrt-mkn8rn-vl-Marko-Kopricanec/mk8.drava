using System.Runtime.ExceptionServices;
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

    public static Task FailsStartupWhenExistingSiteConfigIsInvalidAsync()
    {
        return RunInvalidStartupScenarioAsync();
    }

    public static async Task InvalidStartupExposesShutdownFailureAsync()
    {
        var failure = new IOException("Controlled invalid-startup shutdown failure.");
        var probe = new ShutdownFaultProbe(failure);
        IOException? observed = null;
        try
        {
            await RunInvalidStartupScenarioAsync(probe).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            observed = exception;
        }

        AssertEx.True(ReferenceEquals(failure, observed), "Shutdown fault was swallowed or replaced.");
        AssertEx.Equal(0, probe.StartCalls);
        AssertEx.Equal(1, probe.StopCalls);
    }

    public static async Task InvalidStartupRetainsAssertionAndShutdownFailuresAsync()
    {
        var assertionFailure = new FormatException("Controlled invalid-startup assertion failure.");
        var shutdownFailure = new IOException("Controlled invalid-startup shutdown failure.");
        var probe = new ShutdownFaultProbe(shutdownFailure);
        AggregateException? observed = null;
        try
        {
            await RunInvalidStartupScenarioAsync(probe, async host =>
            {
                await AssertEx.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken.None)).ConfigureAwait(false);
                throw assertionFailure;
            }).ConfigureAwait(false);
        }
        catch (AggregateException exception)
        {
            observed = exception;
        }

        var failures = AssertEx.NotNull(observed).InnerExceptions;
        AssertEx.Equal(2, failures.Count);
        AssertEx.True(ReferenceEquals(assertionFailure, failures[0]));
        AssertEx.True(ReferenceEquals(shutdownFailure, failures[1]));
        AssertEx.Equal(0, probe.StartCalls);
        AssertEx.Equal(1, probe.StopCalls);
    }

    public static async Task InvalidStartupRethrowsAssertionAfterSuccessfulShutdownAsync()
    {
        var failure = new FormatException("Controlled invalid-startup assertion failure.");
        var probe = new ShutdownFaultProbe(failure: null);
        FormatException? observed = null;
        try
        {
            await RunInvalidStartupScenarioAsync(probe, async host =>
            {
                await AssertEx.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken.None)).ConfigureAwait(false);
                throw failure;
            }).ConfigureAwait(false);
        }
        catch (FormatException exception)
        {
            observed = exception;
        }

        AssertEx.True(ReferenceEquals(failure, observed));
        AssertEx.Equal(0, probe.StartCalls);
        AssertEx.Equal(1, probe.StopCalls);
    }

    private static async Task RunInvalidStartupScenarioAsync(IHostedService? shutdownProbe = null, Func<IHost, Task>? startupAssertion = null)
    {
        using var temp = TemporaryDirectory.Create();
        var sites = Directory.CreateDirectory(Path.Combine(temp.Path, "config", "sites")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sites, "broken.json"), "{ nope").ConfigureAwait(false);
        using var host = BuildProxyHost(temp.Path, shutdownProbe);
        ExceptionDispatchInfo? assertionFailure = null;
        try
        {
            if (startupAssertion is null)
            {
                await AssertEx.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(CancellationToken.None)).ConfigureAwait(false);
            }
            else
            {
                await startupAssertion(host).ConfigureAwait(false);
            }
        }
        #pragma warning disable CA1031 // Capture only until the owned host stop is awaited; rethrow the original stack afterward.
        catch (Exception exception)
        {
            assertionFailure = ExceptionDispatchInfo.Capture(exception);
        }
        #pragma warning restore CA1031

        await StopInvalidStartupHostAsync(host, assertionFailure?.SourceException).ConfigureAwait(false);
        assertionFailure?.Throw();
    }

    private static async Task StopInvalidStartupHostAsync(IHost host, Exception? assertionFailure)
    {
        try
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        #pragma warning disable CA1031 // Preserve both exact owned-operation failures when assertion and host stop both fail.
        catch (Exception exception) when (assertionFailure is not null)
        {
            throw new AggregateException("Invalid-startup assertion and cleanup failed.", assertionFailure, exception);
        }
        #pragma warning restore CA1031
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

    private static IHost BuildProxyHost(string dataDirectory, IHostedService? shutdownProbe = null)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [$"{MdravaDataDirectoryOptions.SectionName}:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
        {
            services.AddProxyDataPlane(context.Configuration);
            if (shutdownProbe is not null) services.AddSingleton(shutdownProbe);
        }).Build();
    }

    private sealed class ShutdownFaultProbe(IOException? failure) : IHostedService
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
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
