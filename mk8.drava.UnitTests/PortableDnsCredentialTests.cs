using System.Text.Json;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableDnsCredentialTests
{
    // Canonical encodings of finite synthetic32byte capabilities, never deployment values.
    [Theory]
    [InlineData(42)]
    [InlineData(44)]
    public void NativeCapabilityRejectsWrongEncodedLength(int length)
    {
        var profile = new NativeDnsManagementSettings { Credential = new SecretText(new string('A', length)) };
        Assert.Throws<InvalidDataException>(() => profile.DecodeCredential());
    }

    [Fact]
    public void NativeCapabilityRejectsNoncanonicalPadBits()
    {
        var profile = new NativeDnsManagementSettings { Credential = new SecretText(new string('A', 42) + "B") };
        Assert.Throws<InvalidDataException>(() => profile.DecodeCredential());
    }

    [Fact]
    public async Task ProtectedCollectionSuppliesNativeCapabilityWithoutACredentialFileAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var configured = new NativeDnsManagementSettings { ProfileId = Guid.NewGuid(), Credential = new SecretText(new string('A', 43)) };
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(configured), CancellationToken.None).ConfigureAwait(true);
        var loaded = await PortableConfiguration.LoadDocumentAsync<NativeDnsManagementSettings>(path, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(new byte[32], loaded.DecodeCredential());
        Assert.DoesNotContain(configured.Credential.Reveal(), loaded.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory.Path, "capability")));
    }

    [Fact]
    public void LegacyLocatorIsNotAProviderFallback()
    {
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<DnsPublicationSettings>("{\"credentialPath\":\"/reserved/fixture\"}"u8.ToArray()));
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<NativeDnsManagementSettings>("{\"credentialPath\":\"/reserved/fixture\"}"u8.ToArray()));
    }
}
