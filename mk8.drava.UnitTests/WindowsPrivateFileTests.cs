using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Storage;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class WindowsPrivateFileTests
{
    [WindowsFact]
    public void LeaseCreationIsPrivateExclusiveAndNeverTruncatesAnExistingFile()
    {
        using var directory = new WindowsPrivateTestDirectory();
        var path = Path.Combine(directory.Path, "writer.lock");
        using (var writer = PrivateOwnerFile.AcquireLease(path))
        {
            writer.Write([1, 2, 3]);
            Assert.Throws<IOException>(() => { using var duplicate = PrivateOwnerFile.AcquireLease(path); });
        }
        using (var writer = PrivateOwnerFile.AcquireLease(path)) Assert.Equal(3, writer.Length);
        Assert.Equal([1, 2, 3], PrivateOwnerFile.Read(path, 3, 3));
        Assert.Throws<InvalidDataException>(() => PrivateOwnerFile.Read(path, 4, 4));
    }

    [WindowsFact]
    public async Task BroadFileAccessRefusesProviderActivationWithoutCreatingAKeyAsync()
    {
        using var directory = new WindowsPrivateTestDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var bytes = Encoding.UTF8.GetBytes(PortableConfiguration.Serialize(new GatewayBootstrap { SiteId = "windows-fixture" }));
        var stream = WindowsPrivateFile.Open(path, lease: true);
        await using (stream.ConfigureAwait(true))
        {
            await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(true);
        }
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null), FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(security);
        await Assert.ThrowsAsync<InvalidDataException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
        Assert.False(File.Exists(Path.Combine(directory.Path, ".installation.key")));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path).ConfigureAwait(true));
        Assert.Throws<InvalidDataException>(() => { using var reader = PrivateOwnerFile.OpenRead(path); });
        Assert.Throws<InvalidDataException>(() => { using var writer = PrivateOwnerFile.AcquireLease(path); });
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path).ConfigureAwait(true));
    }

    [WindowsFact]
    public void BroadParentAccessIsRefusedBeforeLeaseCreation()
    {
        using var directory = new WindowsPrivateTestDirectory();
        var path = Path.Combine(directory.Path, "writer.lock");
        var info = new DirectoryInfo(directory.Path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        info.SetAccessControl(security);
        Assert.Throws<InvalidDataException>(() => { using var writer = PrivateOwnerFile.AcquireLease(path); });
        Assert.False(File.Exists(path));
    }

    [WindowsFact]
    public void AlternateStreamsAliasesAndDevicesAreNeverOpenedAsPrivateFiles()
    {
        using var directory = new WindowsPrivateTestDirectory();
        var path = Path.Combine(directory.Path, "absent");
        foreach (var alias in new[] { path + ":stream", path + ".", path + " ", "\\\\?\\" + path })
            Assert.Throws<InvalidDataException>(() => { using var writer = PrivateOwnerFile.AcquireLease(alias); });
        Assert.False(File.Exists(path));
        foreach (var name in new[] { "NUL", "NUL.txt", "COM1", "COM¹.txt", "LPT²", "CONIN$" })
            Assert.Throws<InvalidDataException>(() => { using var reader = PrivateOwnerFile.OpenRead(Path.Combine(directory.Path, name)); });
        Assert.Throws<InvalidDataException>(() => { using var reader = PrivateOwnerFile.OpenRead(directory.Path); });
    }

    [WindowsFact]
    public async Task NewCertificateMaterialIsPrivateAndAnExistingDestinationIsNeverOverwrittenAsync()
    {
        using var directory = new WindowsPrivateTestDirectory();
        var path = Path.Combine(directory.Path, "certificate.pfx");
        Assert.Throws<InvalidDataException>(() => PrivateCertificateFile.Read(path));
        var bytes = Enumerable.Repeat((byte)7, 128).ToArray();
        await PrivateCertificateFile.WriteNewAsync(path, bytes, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(bytes, PrivateCertificateFile.Read(path));
        await Assert.ThrowsAsync<IOException>(() => PrivateCertificateFile.WriteNewAsync(path, new byte[128], CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(bytes, PrivateCertificateFile.Read(path));
        Assert.Equal([path], Directory.GetFiles(directory.Path));
    }

}
