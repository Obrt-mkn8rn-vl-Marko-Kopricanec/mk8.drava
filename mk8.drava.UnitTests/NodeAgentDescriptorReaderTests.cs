using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Relay;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NodeAgentDescriptorReaderTests
{
    private const string Fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [LinuxFileFact]
    public async Task PrivateDescriptorUsesTheEnrolledIdentityAndMissingFileIsOptionalAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "agent.json");
        var trust = new RegistrationSiteTrust { SiteId = "site", NodeAgentDescriptorPath = path };
        Assert.Null(await NodeAgentLocalClient.ReadDescriptorAsync(trust, "node", Fingerprint, CancellationToken.None).ConfigureAwait(true));
        var descriptor = Descriptor();
        await PortableFixtureFile.WriteAsync(path, BootstrapFile.Serialize(descriptor)).ConfigureAwait(true);
        Assert.Equal(descriptor, await NodeAgentLocalClient.ReadDescriptorAsync(trust, "node", Fingerprint, CancellationToken.None).ConfigureAwait(true));
        await Assert.ThrowsAsync<InvalidDataException>(() => NodeAgentLocalClient.ReadDescriptorAsync(trust, "another-node", Fingerprint, CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [LinuxFileTheory]
    [InlineData("directory")]
    [InlineData("directory-suffix")]
    [InlineData("dangling-link")]
    [InlineData("public-file")]
    [InlineData("public-parent-with-absent-file")]
    [InlineData("executable-file")]
    [InlineData("noncanonical-path")]
    public async Task InvalidDescriptorCustodyCannotBeTreatedAsAnAbsentAgentAsync(string fault)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "agent.json");
        await PortableFixtureFile.WriteAsync(path, BootstrapFile.Serialize(Descriptor())).ConfigureAwait(true);
        switch (fault)
        {
            case "directory": File.Delete(path); Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); break;
            case "directory-suffix": File.Delete(path); Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); path += Path.DirectorySeparatorChar; break;
            case "dangling-link": File.Delete(path); File.CreateSymbolicLink(path, Path.Combine(directory.Path, "absent")); break;
            case "public-file": File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead); break;
            case "public-parent-with-absent-file": File.Delete(path); File.SetUnixFileMode(directory.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead); break;
            case "executable-file": File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); break;
            case "noncanonical-path": path = directory.Path + "/./agent.json"; break;
            default: throw new InvalidOperationException("Unknown descriptor control.");
        }
        var trust = new RegistrationSiteTrust { SiteId = "site", NodeAgentDescriptorPath = path };
        await Assert.ThrowsAsync<InvalidDataException>(() => NodeAgentLocalClient.ReadDescriptorAsync(trust, "node", Fingerprint, CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [LinuxFileFact]
    public async Task ReaderRetainsTheAdmittedFileWhenItsNameIsReplacedAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, "agent.json");
        var original = BootstrapFile.Serialize(Descriptor());
        await PortableFixtureFile.WriteAsync(path, original).ConfigureAwait(true);
        var stream = InstallationDocumentGuard.OpenRead(path, 1, 16 * 1024);
        await using var streamLifetime = stream.ConfigureAwait(true);
        File.Move(path, path + ".old");
        await File.WriteAllTextAsync(path, "replacement", CancellationToken.None).ConfigureAwait(true);
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(original, System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task CancellationIsObservedEvenWhenNoAgentWasConfiguredAsync()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NodeAgentLocalClient.ReadDescriptorAsync(
            new RegistrationSiteTrust(), "node", Fingerprint, canceled.Token).AsTask()).ConfigureAwait(true);
    }

    private static NodeAgentDescriptor Descriptor() => new()
    {
        SiteId = "site", NodeId = "node", AgentBootId = "00000000000000000000000000000001", RelayAddress = "127.0.0.1", RelayPort = 12345,
        CertificateFingerprint = Fingerprint, LocalEndpoint = new IpcEndpoint { NamedPipeName = "synthetic_agent" },
    };
}
