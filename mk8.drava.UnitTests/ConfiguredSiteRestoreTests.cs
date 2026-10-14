using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.Recovery;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ConfiguredSiteRestoreTests
{
    private const long MaximumRegistryBytes = 16 * 1024 * 1024;
    // Loopback and finite unused ports describe an offline configuration fixture; no listener or certificate is created.
    private static readonly RecoveryFileApproval[] Approvals = [new("ipc.token", Credential: true), new("admin.token", Credential: true)];

    [Fact]
    public async Task ProtectedPolicyOnlyRecoveryDoesNotRequireOrdinaryCredentialFilesAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(source.Path, source.Path, "site").ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (role.ConfigureAwait(true))
        {
            var repository = await SqliteRegistryRepository.OpenAsync(source.Path, "site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
            await using (repository.ConfigureAwait(true))
            {
                Assert.True(await repository.TryCommitPolicyAsync(0, 0, new PolicyRevision(1, "{}", DateTimeOffset.UnixEpoch, "fixture", "control"), CancellationToken.None).ConfigureAwait(true));
            }
        }
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var backup = await SiteRecoveryBundle.CreateProtectedAsync(source.Path, gateway.Path, snapshot, "site", MaximumRegistryBytes,
            maximumFileBytes: 1, files: [], path, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(backup.Receipt.Files);
        var destination = Path.Combine(parent.Path, "site");
        var request = Request(snapshot, destination, backup.ManifestSha256) with
        { SchemaVersion = 2, MaximumFileBytes = 1, ApprovedFiles = [], Placements = [], };
        var result = await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(result.Receipt.Files);
        Assert.Empty(result.Receipt.Placements);
        Assert.False(Directory.Exists(Path.Combine(destination, "application", "policy")));
        var restored = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(Path.Combine(destination, "roles", "application", "Environment", ".env"), CancellationToken.None).ConfigureAwait(true);
        await using var restoredLifetime = restored.ConfigureAwait(true);
        var reopened = await SqliteRegistryRepository.OpenAsync(Path.Combine(destination, "application"), "site", new PortablePolicyPayloadStore(restored), CancellationToken.None).ConfigureAwait(true);
        await using var reopenedLifetime = reopened.ConfigureAwait(true);
        Assert.Equal("{}", (await reopened.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true))?.CanonicalJson, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ProtectedPreparationImportsApprovedHistoryBeforeFinalReceiptsAndSurvivesReopenAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var backup = await BackupAsync(source.Path, gateway.Path, snapshot, protectedPolicy: true).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "site");
        var request = Request(snapshot, destination, backup.ManifestSha256) with { SchemaVersion = 2 };
        var result = await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, result.Receipt.SchemaVersion);
        Assert.Null(result.Receipt.Registry.PolicyEscrow);
        Assert.Equal(backup.Receipt.Registry.PolicyEscrow?.References, result.Receipt.PolicyReferences);
        Assert.False(Directory.Exists(Path.Combine(destination, "application", "policy")));
        var applicationPath = Path.Combine(destination, "roles", "application", "Environment", ".env");
        Assert.Equal(result.Receipt.ApplicationBootstrapSha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(applicationPath, CancellationToken.None).ConfigureAwait(true))), StringComparer.Ordinal);
        Assert.Equal(result.Receipt.Registry.DatabaseSha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, "application", "registry.sqlite"), CancellationToken.None).ConfigureAwait(true))), StringComparer.Ordinal);
        for (var reopen = 0; reopen < 2; reopen++)
        {
            var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(applicationPath, CancellationToken.None).ConfigureAwait(true);
            await using var roleLifetime = role.ConfigureAwait(true);
            var repository = await SqliteRegistryRepository.OpenAsync(Path.Combine(destination, "application"), "site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
            await using var repositoryLifetime = repository.ConfigureAwait(true);
            var history = await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(2, history.Count);
            Assert.Equal("{\"synthetic\":2}", history[0].CanonicalJson);
            Assert.Equal("{\"synthetic\":1}", history[1].CanonicalJson);
            Assert.True((await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Grants["node"].Revoked);
            Assert.Equal(result.Receipt.Registry.RegistryRevision, (await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Revision);
        }
        var refused = Request(snapshot, Path.Combine(parent.Path, "wrong-schema"), backup.ManifestSha256);
        await Assert.ThrowsAsync<InvalidDataException>(() => ConfiguredSiteRestore.PrepareAsync(refused, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.False(Directory.Exists(refused.DestinationDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationRelocatesValidatedPairAndRetainsClosedRegistryAcrossReopenAsync(bool configuration)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var backup = await BackupAsync(source.Path, gateway.Path, snapshot, configuration).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "site");
        var request = Request(snapshot, destination, backup.ManifestSha256);
        if (configuration)
        {
            request = request with
            {
                ApprovedFiles = [.. Approvals, new("site.json", Credential: false)],
                Placements = [.. request.Placements, new("site.json", "application/config/sites/site.json")],
            };
        }
        var result = await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true);
        var application = await PortableConfiguration.LoadDocumentAsync<ApplicationBootstrap>(Path.Combine(destination, "roles", "application", "Environment", ".env"), CancellationToken.None).ConfigureAwait(true);
        var restoredGateway = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(Path.Combine(destination, "roles", "gateway", "Environment", ".env"), CancellationToken.None).ConfigureAwait(true);
        application.Validate(); restoredGateway.Validate();
        Assert.Equal(1, result.Receipt.SchemaVersion);
        Assert.Equal(Path.Combine(destination, "application"), application.StateDirectory, StringComparer.Ordinal);
        Assert.Equal(application.Listen, restoredGateway.Application);
        Assert.Equal(backup.Receipt.Registry.RegistryRevision + 1, result.Receipt.Registry.RegistryRevision);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, "recovery.json"), CancellationToken.None).ConfigureAwait(true))), result.ReceiptSha256, StringComparer.Ordinal);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, "roles", "application", "Environment", ".env"), CancellationToken.None).ConfigureAwait(true))), result.Receipt.ApplicationBootstrapSha256, StringComparer.Ordinal);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, "roles", "gateway", "Environment", ".env"), CancellationToken.None).ConfigureAwait(true))), result.Receipt.GatewayBootstrapSha256, StringComparer.Ordinal);
        Assert.False(File.Exists(application.Listen.UnixSocketPath));
        Assert.Empty(Directory.GetFileSystemEntries(restoredGateway.StateDirectory));
        Assert.False(Directory.Exists(Path.Combine(destination, "recovered")));
        foreach (var file in result.Receipt.Files)
        {
            var placement = request.Placements.First(entry => string.Equals(entry.Name, file.Name, StringComparison.Ordinal));
            var path = Path.Combine(destination, placement.RelativePath);
            Assert.Equal(file.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true))), StringComparer.Ordinal);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        for (var reopen = 0; reopen < 2; reopen++)
        {
            var repository = await SqliteRegistryRepository.OpenAsync(application.StateDirectory, "site", CancellationToken.None).ConfigureAwait(true);
            await using var lifetime = repository.ConfigureAwait(true);
            var state = await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.True(state.Grants["node"].Revoked);
            Assert.Equal(result.Receipt.Registry.RegistryRevision, state.Revision);
        }
    }

    [Theory]
    [InlineData("site")]
    [InlineData("state")]
    [InlineData("ipc")]
    [InlineData("port")]
    [InlineData("external")]
    [InlineData("alias")]
    [InlineData("runtime")]
    [InlineData("placement")]
    [InlineData("classification")]
    [InlineData("digest")]
    [InlineData("escrow")]
    [InlineData("null-file")]
    [InlineData("null-placement")]
    public async Task IncompatibleInputNeverPublishesOrChangesSourceRegistryAsync(string defect)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var backup = await BackupAsync(source.Path, gateway.Path, snapshot).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "site");
        var request = Request(snapshot, destination, backup.ManifestSha256);
        request = defect switch
        {
            "site" => request with { Gateway = request.Gateway with { SiteId = "different" } },
            "state" => request with { Application = request.Application with { StateDirectory = source.Path } },
            "ipc" => request with { Gateway = request.Gateway with { Application = request.Gateway.Application with { UnixSocketPath = Path.Combine(destination, "different.sock") } } },
            "port" => request with { Gateway = request.Gateway with { HttpPort = 18082 } },
            "external" => request with { Application = request.Application with { AdministratorToken = new SecretText(Path.Combine(source.Path, "admin.token")) } },
            "alias" => request with { Application = request.Application with { AdministratorToken = request.Application.IpcToken } },
            "runtime" => request with { Placements = [request.Placements[0] with { RelativePath = "gateway/gateway-serving.plan" }, request.Placements[1]] },
            "placement" => request with { Placements = [request.Placements[0] with { RelativePath = "material/../escape" }, request.Placements[1]] },
            "classification" => request with { ApprovedFiles = [Approvals[0] with { Credential = false }, Approvals[1]] },
            "digest" => request with { ExpectedManifestSha256 = new string('A', 64) },
            "escrow" => request with { AllowCredentialEscrow = false },
            "null-file" or "null-placement" => MalformedInventory(request, defect),
            _ => throw new InvalidOperationException("Unknown fixture."),
        };
        var before = await File.ReadAllBytesAsync(Path.Combine(snapshot, "registry", "registry.sqlite"), CancellationToken.None).ConfigureAwait(true);
        if (string.Equals(defect, "ipc", StringComparison.Ordinal))
            await Assert.ThrowsAsync<NotSupportedException>(async () => await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        else
            await Assert.ThrowsAsync<InvalidDataException>(async () => await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(Directory.Exists(destination));
        Assert.Equal([snapshot], Directory.GetDirectories(parent.Path));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(snapshot, "registry", "registry.sqlite"), CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task CancelledPreparationAndExistingDestinationNeverOverwriteAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var backup = await BackupAsync(source.Path, gateway.Path, snapshot).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "site");
        var request = Request(snapshot, destination, backup.ManifestSha256);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ConfiguredSiteRestore.PrepareAsync(request, cancellation.Token).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal([snapshot], Directory.GetDirectories(parent.Path));
        Directory.CreateDirectory(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var marker = Path.Combine(destination, "owner");
        await File.WriteAllTextAsync(marker, "finite owner marker", CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<IOException>(async () => await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("finite owner marker", await File.ReadAllTextAsync(marker, CancellationToken.None).ConfigureAwait(true), StringComparer.Ordinal);
    }

    private static ConfiguredSiteRestoreRequest MalformedInventory(ConfiguredSiteRestoreRequest request, string defect)
    {
        var document = JsonNode.Parse(BootstrapFile.Serialize(request)) ?? throw new InvalidOperationException("Missing fixture JSON.");
        var member = string.Equals(defect, "null-file", StringComparison.Ordinal) ? "approvedFiles" : "placements";
        var array = document[member]?.AsArray() ?? throw new InvalidOperationException("Missing fixture inventory.");
        array[0] = null;
        return BootstrapFile.Parse<ConfiguredSiteRestoreRequest>(Encoding.UTF8.GetBytes(document.ToJsonString()));
    }

    private static ConfiguredSiteRestoreRequest Request(string source, string destination, string digest)
    {
        var ipc = new IpcEndpoint { UnixSocketPath = Path.Combine(destination, "application", "i") };
        return new ConfiguredSiteRestoreRequest
        {
            SchemaVersion = 1, SourceDirectory = source, DestinationDirectory = destination, ExpectedManifestSha256 = digest,
            MaximumRegistryBytes = MaximumRegistryBytes, MaximumFileBytes = 4096, AllowCredentialEscrow = true,
            Application = new ApplicationBootstrap { SiteId = "site", NodeId = "node", StateDirectory = Path.Combine(destination, "application"), Listen = ipc,
                IngressAddress = IPAddress.Loopback.ToString(), HttpPort = 18080, HttpsPort = 0, IpcToken = new SecretText(new string('A', 64)), AdministratorToken = new SecretText(new string('B', 64)), },
            Gateway = new GatewayBootstrap { SiteId = "site", StateDirectory = Path.Combine(destination, "gateway"), Application = ipc, ApplicationToken = new SecretText(new string('A', 64)),
                BindAddress = IPAddress.Loopback.ToString(), HttpPort = 18080, HttpsPort = 0, RegistrationPort = 18081, DiscoveryEnabled = false, },
            ApprovedFiles = Approvals,
            Placements = [new("ipc.token", "material/ipc.token"), new("admin.token", "material/admin.token")],
        };
    }

    private static async Task<SiteRecoveryResult> BackupAsync(string application, string gateway, string destination, bool configuration = false, bool protectedPolicy = false)
    {
        var repository = await SqliteRegistryRepository.OpenAsync(application, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            var grant = new NodeGrant("node", "owner", new string('A', 64), "service", [IPAddress.Loopback.ToString()], 18080, 18081, DateTimeOffset.UnixEpoch.AddYears(100), revoked: false);
            await repository.CommitAsync(0, new RegistryState(1, new Dictionary<string, NodeGrant>(StringComparer.Ordinal) { ["node"] = grant }, RegistryState.Empty.Instances, []),
                new RegistryAudit(DateTimeOffset.UnixEpoch, "fixture", "owner", "site"), CancellationToken.None).ConfigureAwait(true);
        }
        var inputs = new List<RecoveryFileInput>();
        foreach (var name in new[] { "ipc.token", "admin.token" })
        {
            var path = Path.Combine(application, name);
            var bytes = Encoding.UTF8.GetBytes(new string(string.Equals(name, "ipc.token", StringComparison.Ordinal) ? 'A' : 'B', 64));
            await File.WriteAllBytesAsync(path, bytes, CancellationToken.None).ConfigureAwait(true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            inputs.Add(new RecoveryFileInput(path, name, Credential: true));
        }
        if (configuration)
        {
            var path = Path.Combine(application, "site.json");
            // Reserved host and loopback are finite offline manual-route fixtures, never deployment assignments.
            var bytes = Encoding.UTF8.GetBytes("{\"name\":\"fixture\",\"host\":\"restored.invalid\",\"listeners\":[{\"name\":\"http\",\"address\":\"127.0.0.1\",\"port\":18080}],\"pathPrefix\":\"/\",\"upstreams\":[{\"name\":\"fixture\",\"address\":\"127.0.0.1\",\"port\":18081,\"scheme\":\"http\"}]}");
            await File.WriteAllBytesAsync(path, bytes, CancellationToken.None).ConfigureAwait(true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            inputs.Add(new RecoveryFileInput(path, "site.json", Credential: false));
        }
        if (protectedPolicy)
        {
            var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(application, application, "site").ConfigureAwait(true);
            var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
            await using (role.ConfigureAwait(true))
            {
                var protectedRepository = await SqliteRegistryRepository.OpenAsync(application, "site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
                await using (protectedRepository.ConfigureAwait(true))
                {
                    Assert.True(await protectedRepository.TryCommitPolicyAsync(0, 1, new PolicyRevision(1, "{\"synthetic\":1}", DateTimeOffset.UnixEpoch, "operator", "control"), CancellationToken.None).ConfigureAwait(true));
                    Assert.True(await protectedRepository.TryCommitPolicyAsync(1, 1, new PolicyRevision(2, "{\"synthetic\":2}", DateTimeOffset.UnixEpoch, "operator", "control"), CancellationToken.None).ConfigureAwait(true));
                }
            }
            return await SiteRecoveryBundle.CreateProtectedAsync(application, gateway, destination, "site", MaximumRegistryBytes, 4096, inputs,
                path, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        }
        return await SiteRecoveryBundle.CreateAsync(application, gateway, destination, "site", MaximumRegistryBytes, 4096, inputs, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
    }
}
