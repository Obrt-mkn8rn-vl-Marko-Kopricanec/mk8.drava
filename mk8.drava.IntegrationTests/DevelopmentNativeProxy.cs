using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentNativeProxy : IAsyncDisposable
{
    private readonly string _directory;
    private readonly DevelopmentProcess _process;
    public int Port { get; }

    private DevelopmentNativeProxy(string directory, DevelopmentProcess process, int port)
    {
        _directory = directory;
        _process = process;
        Port = port;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The local process is disposed on startup failure and transferred to the returned IAsyncDisposable fixture on success; its private constructor only assigns fields. CA2000 does not model this async ownership transfer.")]
    public static async Task<DevelopmentNativeProxy> StartAsync(int originPort)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "drava_native_" + Guid.NewGuid().ToString("N"))).FullName;
        DevelopmentProcess? process = null;
        try
        {
            var sites = Directory.CreateDirectory(Path.Combine(directory, "config", "sites")).FullName;
            var port = TwoProcessProxy.UnusedPort();
            await File.WriteAllTextAsync(Path.Combine(sites, "reference.json"), JsonSerializer.Serialize(new
            {
                name = "test", host = "svc.site.test", pathPrefix = "/",
                listeners = new[] { new { name = "http", address = "127.0.0.1", port } },
                upstreams = new[] { new { name = "test", address = "127.0.0.1", port = originPort } },
            })).ConfigureAwait(false);
            process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.CompatibilityTests"), directory, "--development-native-reference");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await WaitForListenerAsync(process, port, deadline.Token).ConfigureAwait(false);
            var result = new DevelopmentNativeProxy(directory, process, port);
            process = null;
            return result;
        }
        catch
        {
            if (process is not null) await process.DisposeAsync().ConfigureAwait(false);
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private static async Task WaitForListenerAsync(DevelopmentProcess process, int port, CancellationToken cancellationToken)
    {
        while (true)
        {
            process.ThrowIfExited();
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SocketException) { await Task.Delay(50, cancellationToken).ConfigureAwait(false); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync().ConfigureAwait(false);
        try
        {
            var evidence = Directory.CreateDirectory(Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "native-reference-tests", Path.GetFileName(_directory))).FullName;
            await File.WriteAllTextAsync(Path.Combine(evidence, "native.log"), _process.CapturedLog).ConfigureAwait(false);
        }
        finally { Directory.Delete(_directory, recursive: true); }
    }
}
