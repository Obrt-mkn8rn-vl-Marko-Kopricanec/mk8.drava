using System.Net.Sockets;
using Mk8.Drava.Application.DAL.Transport;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PrivateUnixSocketTests
{
    [Fact]
    public async Task StaleOwnedSocketCanBeReboundAfterJoinedProcessExitAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "app.sock");
        using (var old = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)) old.Bind(new UnixDomainSocketEndPoint(path));
        await PrivateUnixSocket.PrepareAsync(path, CancellationToken.None).ConfigureAwait(true);
        using var replacement = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        replacement.Bind(new UnixDomainSocketEndPoint(path));
    }

    [Fact]
    public async Task LiveListenerIsPreservedAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "app.sock");
        using var live = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        live.Bind(new UnixDomainSocketEndPoint(path));
        live.Listen(4);
        await Assert.ThrowsAsync<IOException>(async () => await PrivateUnixSocket.PrepareAsync(path, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        using var accepted = await live.AcceptAsync().ConfigureAwait(true);
        Assert.True(accepted.Connected);
    }

    [Fact]
    public async Task RegularFileAndSymlinkArePreservedAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "app.sock");
        await File.WriteAllTextAsync(path, "preserve").ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await PrivateUnixSocket.PrepareAsync(path, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("preserve", await File.ReadAllTextAsync(path).ConfigureAwait(true));
        var link = Path.Combine(directory.Path, "link.sock");
        File.CreateSymbolicLink(link, path);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await PrivateUnixSocket.PrepareAsync(link, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(path, new FileInfo(link).LinkTarget);
    }
}
