using System.Diagnostics;
using System.Text;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.Transport;
using Mk8.Drava.Transport.Clients;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class IpcIdentityFileTests
{
    // Finite synthetic IPC credential; constructors do not connect or start a listener.
    private static readonly string Token = new('A', 64);

    [Theory]
    [InlineData("mode")]
    [InlineData("link")]
    [InlineData("parent")]
    [InlineData("canonical")]
    public void ClientRefusesUnqualifiedPrivateIdentity(string defect)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        File.WriteAllText(path, Token);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        if (string.Equals(defect, "mode", StringComparison.Ordinal)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        if (string.Equals(defect, "link", StringComparison.Ordinal))
        {
            var link = Path.Combine(directory.Path, "alias.token");
            File.CreateSymbolicLink(link, path); path = link;
        }
        if (string.Equals(defect, "parent", StringComparison.Ordinal)) File.SetUnixFileMode(directory.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        if (string.Equals(defect, "canonical", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.Combine(directory.Path, "child"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            path = Path.Combine(directory.Path, "child", "..", "identity.token");
        }
        var endpoint = new IpcEndpoint { UnixSocketPath = Path.Combine(directory.Path, "i"), IdentityTokenPath = path };
        Assert.Throws<InvalidDataException>(() => { using var client = new ApplicationChannel(endpoint); });
        Assert.Throws<InvalidDataException>(() => new IpcIdentityInterceptor(new ApplicationBootstrap { Listen = endpoint }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClientAcceptsQualifiedIdentityAndPreservesItsExactAuthorization(bool readOnly)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        File.WriteAllText(path, Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | (readOnly ? UnixFileMode.None : UnixFileMode.UserWrite));
        var endpoint = OperatingSystem.IsWindows()
            ? new IpcEndpoint { NamedPipeName = "identity-fixture", IdentityTokenPath = path }
            : new IpcEndpoint { UnixSocketPath = Path.Combine(directory.Path, "i"), IdentityTokenPath = path };
        using var client = new ApplicationChannel(endpoint);
        Assert.Equal("Bearer " + Token, client.Credentials.GetValue("authorization"), StringComparer.Ordinal);
        _ = new IpcIdentityInterceptor(new ApplicationBootstrap { Listen = endpoint });
    }

    [Theory]
    [InlineData(31)]
    [InlineData(257)]
    [InlineData(1048576)]
    public void RefusesOutOfBoundsIdentityWithoutReadingItsContents(int length)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        File.WriteAllText(path, new string('A', length));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Throws<InvalidDataException>(() => PrivateIpcIdentityFile.Read(path));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(256, false)]
    [InlineData(64, true)]
    public void PreservesExistingTokenBoundsBomDetectionAndWhitespace(int length, bool unicode)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        var token = new string('A', length - 2) + "_-";
        if (unicode) File.WriteAllText(path, "\n" + token + "\r\n", Encoding.Unicode);
        else File.WriteAllText(path, token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead);
        Assert.Equal(token, PrivateIpcIdentityFile.Read(path), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("é")]
    [InlineData("\0")]
    public void RefusesNonTokenCharacters(string suffix)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        File.WriteAllText(path, Token + suffix);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Throws<InvalidDataException>(() => PrivateIpcIdentityFile.Read(path));
    }

    [Fact]
    public async Task RefusesFifoWithoutWaitingForAWriterAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        var start = new ProcessStartInfo("mkfifo") { UseShellExecute = false };
        start.ArgumentList.Add("--mode=600");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not create the owned FIFO control.");
        await process.WaitForExitAsync().ConfigureAwait(true);
        Assert.Equal(0, process.ExitCode);
        var read = Task.Run(() => Assert.Throws<InvalidDataException>(() => PrivateIpcIdentityFile.Read(path)));
        await read.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
    }

}
