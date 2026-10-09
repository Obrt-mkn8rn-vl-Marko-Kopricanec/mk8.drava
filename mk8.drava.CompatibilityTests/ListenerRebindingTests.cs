using System.Net;
using System.Net.Sockets;
using System.Text;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ListenerRebindingTests
{
    public static async Task RouteOnlyReloadDoesNotRebindUnchangedListenerAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpPort();
        WriteSite(temp.Path, [new ListenerSpec("main", proxyPort)], GetFreeTcpPort());
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            var before = await WaitForListenerAsync(runtime, "main", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            WriteSite(temp.Path, [new ListenerSpec("main", proxyPort)], GetFreeTcpPort());
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var after = await WaitForListenerAsync(runtime, "main", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var listenerReload = ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors)).ListenerReload;
            AssertEx.Equal(0, listenerReload.Added);
            AssertEx.Equal(0, listenerReload.Removed);
            AssertEx.Equal(0, listenerReload.Changed);
            AssertEx.Equal(1, listenerReload.Unchanged);
            AssertEx.Equal(before.StartedAtUtc, after.StartedAtUtc);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task AddingListenerStartsOnlyNewListenerAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var firstProxyPort = GetFreeTcpPort();
        var secondProxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteSite(temp.Path, [new ListenerSpec("main", firstProxyPort)], upstreamPort);
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            WriteSite(temp.Path, [new ListenerSpec("main", firstProxyPort), new ListenerSpec("extra", secondProxyPort)], upstreamPort);
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            await WaitForListenerAsync(runtime, "extra", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var upstreamTask = RunFixedResponseUpstreamAsync(upstreamPort, "added", timeout.Token);
            var response = await SendSingleRequestAsync(secondProxyPort, "GET /added HTTP/1.1\r\nHost: reload.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var listenerReload = ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors)).ListenerReload;
            AssertEx.Equal(1, listenerReload.Added);
            AssertEx.Equal(1, listenerReload.Unchanged);
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.Equal(2, runtime.Snapshot().Listeners.Count(static listener => listener.State == ProxyListenerState.Active));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task RemovingListenerStopsAcceptingNewConnectionsAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var firstProxyPort = GetFreeTcpPort();
        var secondProxyPort = GetFreeTcpPort();
        WriteSite(temp.Path, [new ListenerSpec("main", firstProxyPort), new ListenerSpec("extra", secondProxyPort)], GetFreeTcpPort());
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            await WaitForConnectAsync(secondProxyPort, shouldSucceed: true, timeout.Token).ConfigureAwait(false);
            WriteSite(temp.Path, [new ListenerSpec("main", firstProxyPort)], GetFreeTcpPort());
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            AssertEx.Equal(1, ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors)).ListenerReload.Removed);
            await WaitForConnectAsync(secondProxyPort, shouldSucceed: false, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task ChangedListenerIsReplacedSafelyAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var oldProxyPort = GetFreeTcpPort();
        var newProxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteSite(temp.Path, [new ListenerSpec("main", oldProxyPort)], upstreamPort);
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            WriteSite(temp.Path, [new ListenerSpec("main", newProxyPort)], upstreamPort);
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            await WaitForListenerAsync(host.Services.GetRequiredService<ProxyRuntimeState>(), "main", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var upstreamTask = RunFixedResponseUpstreamAsync(upstreamPort, "changed", timeout.Token);
            var response = await SendSingleRequestAsync(newProxyPort, "GET /changed HTTP/1.1\r\nHost: reload.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertEx.Equal(1, ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors)).ListenerReload.Changed);
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            await WaitForConnectAsync(oldProxyPort, shouldSucceed: false, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task FailedNewListenerStartPreservesOldActiveListenerAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpPort();
        var occupiedPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var occupied = OccupyPort(occupiedPort);
        WriteSite(temp.Path, [new ListenerSpec("main", proxyPort)], upstreamPort);
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            WriteSite(temp.Path, [new ListenerSpec("main", proxyPort), new ListenerSpec("blocked", occupiedPort)], upstreamPort);
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var upstreamTask = RunFixedResponseUpstreamAsync(upstreamPort, "old-live", timeout.Token);
            var response = await SendSingleRequestAsync(proxyPort, "GET /old HTTP/1.1\r\nHost: reload.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            var failedReload = ProxyConfigurationReloadResultAssertions.ListenerReloadFailed(reload);
            AssertEx.Equal(1, host.Services.GetRequiredService<IProxyConfigurationStore>().Snapshot.Version);
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.True(failedReload.ListenerReload.Errors.Count > 0);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task FailedConfigReloadPreservesOldActiveListenersAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        WriteSite(temp.Path, [new ListenerSpec("main", proxyPort)], upstreamPort);
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            ConfigurationTests.WriteCustomSite(temp.Path, "broken.json", "{ nope");
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var upstreamTask = RunFixedResponseUpstreamAsync(upstreamPort, "still-live", timeout.Token);
            var response = await SendSingleRequestAsync(proxyPort, "GET /still HTTP/1.1\r\nHost: reload.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            ProxyConfigurationReloadResultAssertions.Failed(reload);
            AssertEx.Equal(1, host.Services.GetRequiredService<IProxyConfigurationStore>().Snapshot.Version);
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task CertificateOnlyUpdateDoesNotRebindListenerAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpPort();
        var certificatePath = Path.Combine(temp.Path, "certs", "home.pfx");
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        TestCertificates.WriteSelfSignedPfx(certificatePath, "home.test", "secret");
        ConfigurationTests.WriteHttpsSite(temp.Path, "home.json", proxyPort, GetFreeTcpPort(), "home-cert");
        ConfigurationTests.WriteOperationalConfig(temp.Path, certificateId: "home-cert", certificatePath: "certs/home.pfx", certificatePassword: "secret");
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var runtime = host.Services.GetRequiredService<ProxyRuntimeState>();
            var before = await WaitForListenerAsync(runtime, "main", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            TestCertificates.WriteSelfSignedPfx(certificatePath, "home.test", "secret");
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var after = await WaitForListenerAsync(runtime, "main", ProxyListenerState.Active, timeout.Token).ConfigureAwait(false);
            var listenerReload = ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors)).ListenerReload;
            AssertEx.Equal(0, listenerReload.Added);
            AssertEx.Equal(0, listenerReload.Removed);
            AssertEx.True(listenerReload.Unchanged >= 1, listenerReload.Unchanged.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AssertEx.Equal(before.StartedAtUtc, after.StartedAtUtc);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task AdminBindIsNotAffectedByProxyListenerReloadAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpPort();
        WriteSite(temp.Path, [new ListenerSpec("main", proxyPort)], GetFreeTcpPort());
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var before = host.Services.GetRequiredService<IProxyConfigurationStore>().Snapshot.AdminSecurity.Urls;
            WriteSite(temp.Path, [new ListenerSpec("main", GetFreeTcpPort())], GetFreeTcpPort());
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var after = host.Services.GetRequiredService<IProxyConfigurationStore>().Snapshot.AdminSecurity.Urls;
            var reloaded = ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors));
            AssertEx.Equal(string.Join('|', before), string.Join('|', after));
            AssertEx.Equal(1, reloaded.ListenerReload.Changed);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task ReloadDiagnosticsReportListenerDiffAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var oldMainPort = GetFreeTcpPort();
        var newMainPort = GetFreeTcpPort();
        var removedPort = GetFreeTcpPort();
        var addedPort = GetFreeTcpPort();
        WriteSite(temp.Path, [new ListenerSpec("main", oldMainPort), new ListenerSpec("removed", removedPort)], GetFreeTcpPort());
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            WriteSite(temp.Path, [new ListenerSpec("main", newMainPort), new ListenerSpec("added", addedPort)], GetFreeTcpPort());
            var reload = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var listenerReload = ProxyConfigurationReloadResultAssertions.Reloaded(reload, string.Join("; ", reload.Errors)).ListenerReload;
            AssertEx.Equal(1, listenerReload.Added);
            AssertEx.Equal(1, listenerReload.Removed);
            AssertEx.Equal(1, listenerReload.Changed);
            AssertEx.True(listenerReload.Changes.Any(static change => string.Equals(change.Action, "added", StringComparison.Ordinal) && string.Equals(change.Name, "added", StringComparison.Ordinal)));
            AssertEx.True(listenerReload.Changes.Any(static change => string.Equals(change.Action, "removed", StringComparison.Ordinal) && string.Equals(change.Name, "removed", StringComparison.Ordinal)));
            AssertEx.True(listenerReload.Changes.Any(static change => string.Equals(change.Action, "changed", StringComparison.Ordinal) && string.Equals(change.Name, "main", StringComparison.Ordinal)));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task MetricsCountListenerReloadOutcomesAsync()
    {
        using var temp = TemporaryDirectory.Create();
        var proxyPort = GetFreeTcpPort();
        var occupiedPort = GetFreeTcpPort();
        using var occupied = OccupyPort(occupiedPort);
        WriteSite(temp.Path, [new ListenerSpec("main", proxyPort)], GetFreeTcpPort());
        using var host = BuildProxyHost(temp.Path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            WriteSite(temp.Path, [new ListenerSpec("main", proxyPort), new ListenerSpec("extra", GetFreeTcpPort())], GetFreeTcpPort());
            var success = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            WriteSite(temp.Path, [new ListenerSpec("main", proxyPort), new ListenerSpec("blocked", occupiedPort)], GetFreeTcpPort());
            var failure = await host.Services.GetRequiredService<IProxyConfigurationReloadOperations<ProxyConfigurationProjection>>().ReloadAsync(timeout.Token).ConfigureAwait(false);
            var metrics = host.Services.GetRequiredService<ProxyMetrics>().Snapshot();
            ProxyConfigurationReloadResultAssertions.Reloaded(success, string.Join("; ", success.Errors));
            ProxyConfigurationReloadResultAssertions.ListenerReloadFailed(failure);
            AssertEx.True(metrics.Listeners.ReloadAttempts >= 3);
            AssertEx.True(metrics.Listeners.ReloadSuccesses >= 2);
            AssertEx.True(metrics.Listeners.ReloadFailures >= 1);
            AssertEx.True(metrics.Listeners.ReloadAdded >= 1);
            AssertEx.True(metrics.Listeners.StartFailures >= 1);
            AssertEx.True(metrics.Listeners.ActiveListeners >= 1);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static IHost BuildProxyHost(string dataDirectory)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [$"{MdravaDataDirectoryOptions.SectionName}:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) => services.AddProxyDataPlane(context.Configuration)).Build();
    }

    private static void WriteSite(string dataDirectory, IReadOnlyList<ListenerSpec> listeners, int upstreamPort)
    {
        var sites = Directory.CreateDirectory(Path.Combine(dataDirectory, "config", "sites")).FullName;
        var listenerJson = string.Join(",\n", listeners.Select(static listener => $$"""
                    {
                      "name": "{{listener.Name}}",
                      "address": "127.0.0.1",
                      "port": {{listener.Port}}
                    }
                """));
        File.WriteAllText(Path.Combine(sites, "rebinding.json"), $$"""
            {
              "name": "rebinding",
              "listeners": [
            {{listenerJson}}
              ],
              "host": "*",
              "pathPrefix": "/",
              "upstreams": [
                {
                  "name": "local-test",
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}}
                }
              ]
            }
            """);
    }

    private static async Task RunFixedResponseUpstreamAsync(int port, string body, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await using var streamDisposal = stream.ConfigureAwait(false);
            await ReadRequestHeadAsync(stream, cancellationToken).ConfigureAwait(false);
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n{body}");
            await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> SendSingleRequestAsync(int port, string request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        await using var streamDisposal = stream.ConfigureAwait(false);
        var bytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        return await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitForConnectAsync(int port, bool shouldSucceed, CancellationToken cancellationToken)
    {
        while (true)
        {
            var connected = await TryConnectAsync(port, cancellationToken).ConfigureAwait(false);
            if (connected == shouldSucceed)
            {
                return;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> TryConnectAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static Socket OccupyPort(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true
        };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        socket.Listen(1);
        return socket;
    }

    private static async Task<ProxyListenerStatus> WaitForListenerAsync(ProxyRuntimeState runtimeState, string name, ProxyListenerState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            var listener = runtimeState.Snapshot().Listeners.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) && candidate.State == state);
            if (listener is not null)
            {
                return listener;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReadRequestHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var state = 0;
        while (state < 4)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            state = (state, buffer[0]) switch
            {
                (0, (byte)'\r') => 1,
                (1, (byte)'\n') => 2,
                (2, (byte)'\r') => 3,
                (3, (byte)'\n') => 4,
                (_, (byte)'\r') => 1,
                _ => 0
            };
        }
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.ASCII.GetString(memory.ToArray());
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

    private sealed record ListenerSpec(string Name, int Port);
    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-rebind-{Guid.NewGuid():N}");
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
            catch
            {
            }
        }
    }
}
