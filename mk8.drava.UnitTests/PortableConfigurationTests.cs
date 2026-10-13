using System.Text.Json;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

[Collection("Portable configuration environment")]
public sealed class PortableConfigurationTests
{
    [Fact]
    public async Task RoleSnapshotIgnoresProcessAndDevelopmentOverridesAndProtectsItsDocumentAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var priorRole = Environment.GetEnvironmentVariable("Drava__GatewayBootstrap");
        var priorEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("Drava__GatewayBootstrap", "{\"siteId\":\"ambient\"}");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
            await File.WriteAllTextAsync(path, PortableConfiguration.Serialize(new GatewayBootstrap { SiteId = "configured" })).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory.Path, ".dev.env"), PortableConfiguration.Serialize(new GatewayBootstrap { SiteId = "overlay" })).ConfigureAwait(true);
            var result = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
            Assert.Equal("configured", result.SiteId, StringComparer.Ordinal);
            Assert.True(File.Exists(Path.Combine(directory.Path, ".installation.key")));
            var store = new SupprocomSecretFileStore(new SupprocomSecretFileOptions
            {
                Directory = directory.Path, Protection = SecretFileProtection.InstallationBoundAesGcm,
            }, "Production");
            Assert.Equal(SecretFileProtectionState.Protected, await store.GetStateAsync(CancellationToken.None).ConfigureAwait(true));
            await store.SetAsync("Drava:GatewayBootstrap", "{\"siteId\":\"replacement\"}", CancellationToken.None).ConfigureAwait(true);
            Assert.Equal("configured", result.SiteId, StringComparer.Ordinal);
            Assert.Equal("replacement", (await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true)).SiteId, StringComparer.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Drava__GatewayBootstrap", priorRole);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", priorEnvironment);
        }
    }

    [Fact]
    public async Task ADifferentRoleCannotSubstituteForTheRequiredRoleAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await File.WriteAllTextAsync(path, PortableConfiguration.Serialize(new ApplicationBootstrap())).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task MissingActiveCollectionDoesNotActivateATemplateOrCreateAKeyAsync()
    {
        using var directory = new RegistryStateDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, ".env.template"), PortableConfiguration.Serialize(new GatewayBootstrap())).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(Path.Combine(directory.Path, ".env"), CancellationToken.None)).ConfigureAwait(true);
        Assert.False(File.Exists(Path.Combine(directory.Path, ".env")));
        Assert.False(File.Exists(Path.Combine(directory.Path, ".installation.key")));
    }

    [Theory]
    [InlineData("--bootstrap")]
    [InlineData("--secrets-directory")]
    [InlineData("--secrets-file")]
    public void CommandLineServiceLocationsAreRefused(string option)
    {
        Assert.Throws<ArgumentException>(() => PortableConfiguration.StartupPath([option, Path.GetTempPath()]));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Environment", ".env"), PortableConfiguration.StartupPath([]), StringComparer.Ordinal);
    }

    [Fact]
    public async Task RawJsonIsRefusedWithoutImportAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await File.WriteAllTextAsync(path, "{\"siteId\":\"legacy\"}").ConfigureAwait(true);
        await Assert.ThrowsAsync<SupprocomSecretsException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
        Assert.False(File.Exists(path + ".original"));
    }

    [Fact]
    public async Task ExternalProviderPointerIsRefusedWithoutOpeningAnyAdapterAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await File.WriteAllTextAsync(path, "SUPPROCOM_SECRET_SOURCE=unavailable://reserved.invalid/fixture\n").ConfigureAwait(true);
        var exception = await Assert.ThrowsAsync<SupprocomSecretsException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal("ExternalProviderPointer", exception.Code, StringComparer.Ordinal);
    }

    [Fact]
    public async Task DuplicateRoleFieldsRetainStrictValidationAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await File.WriteAllTextAsync(path, PortableConfiguration.SerializeDocument<GatewayBootstrap>("{\"siteId\":\"one\",\"siteId\":\"two\"}")).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task UnknownRoleFieldsRetainStrictValidationAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        await File.WriteAllTextAsync(path, PortableConfiguration.SerializeDocument<GatewayBootstrap>("{\"unknown\":1}")).ConfigureAwait(true);
        await Assert.ThrowsAsync<JsonException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
    }
}

