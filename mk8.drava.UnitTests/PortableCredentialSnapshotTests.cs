using System.Text.Json;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableCredentialSnapshotTests
{
    // Finite synthetic capability, not a shipped default or deployment credential.
    private const string Token = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz-";

    [Fact]
    public async Task ProtectedRoleCarriesTheCredentialWithoutASeparateFileOrPlaintextDiagnosticAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var configured = new GatewayBootstrap { ApplicationToken = new SecretText(Token) };
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(configured), CancellationToken.None).ConfigureAwait(true);
        var loaded = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(Token, loaded.ApplicationToken.RequireToken(), StringComparer.Ordinal);
        Assert.DoesNotContain(Token, loaded.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, await File.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true), StringComparison.Ordinal);
        string[] files = [.. Directory.GetFiles(directory.Path).Select(static path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
        Assert.Equal([".env", ".installation.key"], files);
    }

    [Theory]
    [InlineData("identityTokenPath")]
    [InlineData("administratorTokenPath")]
    public void LegacyCredentialLocatorsCannotReenterTheRoleSchema(string property)
    {
        var json = string.Equals(property, "identityTokenPath", StringComparison.Ordinal)
            ? "{\"listen\":{\"identityTokenPath\":\"/reserved/fixture.token\"}}"
            : "{\"administratorTokenPath\":\"/reserved/fixture.token\"}";
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<ApplicationBootstrap>(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("document")]
    [InlineData("key")]
    public async Task SharedReadAccessRefusesTheCollectionBeforeReturningAnyCredentialAsync(string target)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(new GatewayBootstrap { ApplicationToken = new SecretText(Token) }), CancellationToken.None).ConfigureAwait(true);
        _ = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        File.SetUnixFileMode(string.Equals(target, "document", StringComparison.Ordinal) ? path : Path.Combine(directory.Path, ".installation.key"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        await Assert.ThrowsAsync<InvalidDataException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public void SecretNullCannotBecomeAnEmptyOrDefaultCredential()
    {
        Assert.Throws<JsonException>(() => BootstrapFile.Parse<ApplicationBootstrap>("{\"ipcToken\":null}"u8.ToArray()));
    }
}
