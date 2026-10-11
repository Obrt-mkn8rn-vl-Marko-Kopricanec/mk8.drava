using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class MembershipPublicationRetentionTests
{
    [Fact]
    public async Task DrainingOneReplicaKeepsTheOthersOriginalPublicationProofAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        var (first, second) = await InitializeReplicasAsync(runtime).ConfigureAwait(true);
        var proof = Publish(runtime, second);
        Publish(runtime, first);
        await runtime.Registry.DrainAsync(RegistryTestFixture.Fingerprint, first.Identity, CancellationToken.None).ConfigureAwait(true);
        await runtime.Reconciler.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.False(runtime.Availability.IsEligible(first.Identity));
        Assert.True(runtime.Availability.IsEligible(second.Identity));
        Assert.Same(proof, runtime.Availability.Status(second.Identity).Publication);
        Assert.Equal(proof.ValidUntilUtc, runtime.Availability.Status(second.Identity).Publication?.ValidUntilUtc);
    }

    [Fact]
    public async Task APolicyChangeStillClearsPreviouslyValidPublicationAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var runtime = new PolicyRuntime(repository, directory.Path);
        await using var runtimeLifetime = runtime.ConfigureAwait(true);
        var (first, second) = await InitializeReplicasAsync(runtime).ConfigureAwait(true);
        Publish(runtime, first);
        Publish(runtime, second);
        Assert.True(await runtime.Reconciler.UpdatePolicyAsync(1, "{\"site\":{\"algorithm\":\"least-active\"}}", importFile: false, CancellationToken.None).ConfigureAwait(true));
        Assert.Null(runtime.Availability.Status(first.Identity).Publication);
        Assert.Null(runtime.Availability.Status(second.Identity).Publication);
        Assert.False(runtime.Availability.IsEligible(first.Identity));
        Assert.False(runtime.Availability.IsEligible(second.Identity));
    }

    private static async Task<(InstanceIntent First, InstanceIntent Second)> InitializeReplicasAsync(PolicyRuntime runtime)
    {
        await runtime.Registry.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        var grant = new NodeGrant("node", "owner", RegistryTestFixture.Fingerprint, "svc", ["127.0.0.1"], 1024, 65535, DateTimeOffset.UtcNow.AddDays(1), revoked: false);
        await runtime.Registry.EnrollAsync(grant, "administrator", CancellationToken.None).ConfigureAwait(true);
        var first = RegistryTestFixture.Intent();
        var second = RegistryTestFixture.Intent();
        await runtime.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, first, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        await runtime.Registry.RegisterAsync(RegistryTestFixture.Fingerprint, second, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
        await runtime.Reconciler.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        return (first, second);
    }

    private static DestinationPublication Publish(PolicyRuntime runtime, InstanceIntent intent)
    {
        Assert.True(runtime.Availability.SetReadiness(intent.Identity, ready: true, TimeSpan.FromSeconds(120)));
        var proof = new DestinationPublication(runtime.Registry.State.Revision, 1, DnsVerified: true, CertificateVerified: true, DateTimeOffset.UtcNow.AddSeconds(30))
        {
            Address = new PublishedServiceAddress("svc.site.example", "/"),
        };
        Assert.True(runtime.Availability.SetPublication(intent.Identity, proof));
        return proof;
    }
}
