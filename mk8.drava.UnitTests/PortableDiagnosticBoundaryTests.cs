using System.Text.Json;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortableDiagnosticBoundaryTests
{
    private const string Canary = "synthetic-private-configuration-canary";

    [Theory]
    [InlineData("dotenv")]
    [InlineData("role-json")]
    public async Task InitialLoadRetainsItsRefusalTypeWithoutPrivateInputAsync(string fault)
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var document = string.Equals(fault, "dotenv", StringComparison.Ordinal) ? Canary + "@=synthetic\n"
            : PortableConfiguration.SerializeDocument<GatewayBootstrap>(PrivateJson());
        await PortableFixtureFile.WriteAsync(path, document).ConfigureAwait(true);
        Exception failure;
        if (string.Equals(fault, "dotenv", StringComparison.Ordinal))
        {
            var provider = await Assert.ThrowsAsync<SupprocomSecretsException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
            Assert.Equal("InvalidDotenvKey", provider.Code, StringComparer.Ordinal);
            failure = provider;
        }
        else
        {
            failure = await Assert.ThrowsAsync<JsonException>(() => PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None)).ConfigureAwait(true);
        }
        AssertPrivateFailure(failure);
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("material")]
    [InlineData("policy")]
    public async Task OwnedFailureAndShutdownRetainTheSameSafeRefusalWithoutPublishingMaterialAsync(string operation)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var path = await PortablePolicyPayloadStoreTests.InitializeAsync(directory.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        SupprocomSecretsException? failure = null;
        try
        {
            await PortableFixtureFile.WriteAsync(path, Canary + "@=synthetic\n").ConfigureAwait(true);
            var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
            Func<Task> action = operation switch
            {
                "refresh" => () => role.RefreshManualConfigurationAsync(CancellationToken.None).AsTask(),
                "material" => () => role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask(),
                "policy" => () => role.WritePolicyArchiveAsync(PortablePolicyArchive.Empty, new PortablePolicyArchive("replacement"), CancellationToken.None).AsTask(),
                _ => throw new InvalidOperationException("Unknown diagnostic fixture operation."),
            };
            failure = await Assert.ThrowsAsync<SupprocomSecretsException>(action).ConfigureAwait(true);
            Assert.Equal("InvalidDotenvKey", failure.Code, StringComparer.Ordinal);
            AssertPrivateFailure(failure);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(0, role.ReadMaterial().Length);
            Assert.True(role.ReadPolicyDocuments().Archive.IsEmpty);
            Assert.True(role.ReadManualConfiguration().IsEmpty);
        }
        finally
        {
            var close = await Assert.ThrowsAsync<SupprocomSecretsException>(() => role.DisposeAsync().AsTask()).ConfigureAwait(true);
            AssertPrivateFailure(close);
            if (failure is not null) Assert.Same(failure, close);
        }
    }

    [Fact]
    public async Task OwnedUpdateDoesNotRetainTheProvidersPrivateInnerExceptionAsync()
    {
        using var directory = new RegistryStateDirectory();
        var path = Path.Combine(directory.Path, ".env");
        var bootstrap = new GatewayBootstrap();
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(bootstrap)).ConfigureAwait(true);
        var prepared = await PortableConfiguration.LoadRoleDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await prepared.DisposeAsync().ConfigureAwait(true);
        var encoded = BootstrapFile.Serialize(bootstrap);
        var role = new PortableRole<GatewayBootstrap>(bootstrap, new RefusingUpdater(), path, encoded,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Drava:GatewayBootstrap"] = encoded });
        var verified = false;
        SupprocomSecretsException? shutdown = null;
        try
        {
            await using (role.ConfigureAwait(true))
            {
                var failure = await Assert.ThrowsAsync<SupprocomSecretsException>(() => role.WriteMaterialAsync(new SecretBytes(new byte[128]), CancellationToken.None).AsTask()).ConfigureAwait(true);
                Assert.Equal("ProtectionFileReadFailed", failure.Code, StringComparer.Ordinal);
                AssertPrivateFailure(failure);
                Assert.Equal(0, role.ReadMaterial().Length);
                verified = true;
            }
        }
        catch (SupprocomSecretsException failure) when (verified) { shutdown = failure; }
        Assert.NotNull(shutdown);
        AssertPrivateFailure(shutdown);
    }

    [Fact]
    public async Task ApprovedEscrowParsingDoesNotExposePrivateMetadataInputAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var destination = Directory.CreateDirectory(Path.Combine(directory.Path, "escrow"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute).FullName;
        await PortablePolicyEscrow.ExportAsync(destination, "synthetic-site", PortablePolicyArchive.Empty, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        await PortablePolicyPayloadStoreTests.Store(destination).SetAsync("Drava:PolicyEscrow:Metadata", PrivateJson(), CancellationToken.None).ConfigureAwait(true);
        var failure = await Assert.ThrowsAsync<JsonException>(() => PortablePolicyEscrow.LoadAsync(destination, "synthetic-site", allowCredentialEscrow: true, CancellationToken.None)).ConfigureAwait(true);
        AssertPrivateFailure(failure);
    }

    private static string PrivateJson() => JsonSerializer.Serialize(new Dictionary<string, int>(StringComparer.Ordinal) { [Canary] = 1 });

    private static void AssertPrivateFailure(Exception failure)
    {
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(Canary, failure.ToString(), StringComparison.Ordinal);
    }

    private sealed class RefusingUpdater : ISecretDocumentUpdater
    {
        public Task UpdateDocumentAsync(Func<IReadOnlyList<SupprocomSecretSetting>, IReadOnlyList<SupprocomSecretSetting>> update,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException(new SupprocomSecretsException("ProtectionFileReadFailed", Canary, new InvalidOperationException(Canary)));
        }
    }
}
