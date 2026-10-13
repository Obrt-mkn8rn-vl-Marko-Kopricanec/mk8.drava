using System.Diagnostics;
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
    public void CollectionRefusesUnqualifiedPrivateAccess(string defect)
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
        Assert.Throws<InvalidDataException>(() => InstallationDocumentGuard.Require(path, 32, 256));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedCollectionAndConfiguredValuePreserveExactAuthorization(bool readOnly)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        File.WriteAllText(path, Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | (readOnly ? UnixFileMode.None : UnixFileMode.UserWrite));
        InstallationDocumentGuard.Require(path, 32, 256);
        var endpoint = OperatingSystem.IsWindows()
            ? new IpcEndpoint { NamedPipeName = "identity-fixture" }
            : new IpcEndpoint { UnixSocketPath = Path.Combine(directory.Path, "i") };
        using var client = new ApplicationChannel(endpoint, new SecretText(Token));
        Assert.Equal("Bearer " + Token, client.Credentials.GetValue("authorization"), StringComparer.Ordinal);
        _ = new IpcIdentityInterceptor(new ApplicationBootstrap { Listen = endpoint, IpcToken = new SecretText(Token) });
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
        Assert.Throws<InvalidDataException>(() => new SecretText(new string('A', length)).RequireToken());
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(256, false)]
    [InlineData(64, true)]
    public void PreservesTokenBoundsAndRefusesWhitespaceInsteadOfLegacyFileNormalization(int length, bool unicode)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "identity.token");
        var token = new string('A', length - 2) + "_-";
        if (unicode) Assert.Throws<InvalidDataException>(() => new SecretText("\n" + token + "\r\n").RequireToken());
        else Assert.Equal(token, new SecretText(token).RequireToken(), StringComparer.Ordinal);
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
        Assert.Throws<InvalidDataException>(() => new SecretText(Token + suffix).RequireToken());
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
        var read = Task.Run(() => Assert.Throws<InvalidDataException>(() => InstallationDocumentGuard.Require(path, 1, 1024)));
        await read.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
    }

}
