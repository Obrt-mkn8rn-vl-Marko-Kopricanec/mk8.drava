using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistryPersistenceTests
{
    [Fact]
    public async Task RestartLoadsIntentAndOwnershipWithoutResurrectingEligibilityAsync()
    {
        using var directory = new RegistryStateDirectory();
        using var fixture = new RegistryTestFixture();
        var intent = RegistryTestFixture.Intent();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (repository.ConfigureAwait(true))
        {
            using var controller = new RegistryCoordinator(repository, fixture.Availability, fixture.Clock);
            await controller.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            await controller.EnrollAsync(fixture.Grant, "administrator", CancellationToken.None).ConfigureAwait(true);
            await controller.RegisterAsync(RegistryTestFixture.Fingerprint, intent, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
            fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(120));
            fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(controller.State.Revision, 1, true, true, fixture.Clock.GetUtcNow().AddHours(1)) { Address = new PublishedServiceAddress("svc.site.example", "/") });
            Assert.True(fixture.Availability.IsEligible(intent.Identity));
        }
        var restored = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using (restored.ConfigureAwait(true))
        {
            var availability = new DestinationAvailabilityStore(fixture.Clock);
            using var restarted = new RegistryCoordinator(restored, availability, fixture.Clock);
            await restarted.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(2, restarted.State.Revision);
            Assert.Equal(intent.Identity, restarted.State.Instances[intent.Identity.InstanceId].Identity);
            Assert.Equal(RegistryTestFixture.Fingerprint, restarted.State.Grants["node"].CertificateFingerprint);
            Assert.False(availability.IsEligible(intent.Identity));
            await restarted.RenewAsync(RegistryTestFixture.Fingerprint, intent.Identity, TimeSpan.FromSeconds(90), CancellationToken.None).ConfigureAwait(true);
            Assert.False(availability.IsEligible(intent.Identity));
        }
    }

    [Fact]
    public async Task SecondWriterAndStaleRevisionAreRejectedAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        await Assert.ThrowsAsync<IOException>(async () => await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var first = new RegistryState(1, RegistryState.Empty.Grants, RegistryState.Empty.Instances, []);
        await repository.CommitAsync(0, first, new RegistryAudit(DateTimeOffset.UtcNow, "test", "administrator", "site"), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await repository.CommitAsync(0, first, new RegistryAudit(DateTimeOffset.UtcNow, "test", "administrator", "site"), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(1, (await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Revision);
    }

    [Fact]
    public async Task AnotherSiteCannotAdoptStateAndFailedInitializationReleasesWriterLockAsync()
    {
        using var directory = new RegistryStateDirectory();
        var original = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await original.DisposeAsync().ConfigureAwait(true);
        for (var attempt = 0; attempt < 2; attempt++)
            await Assert.ThrowsAsync<InvalidDataException>(async () => await SqliteRegistryRepository.OpenAsync(directory.Path, "other-site", CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var recovered = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var recoveredLifetime = recovered.ConfigureAwait(true);
        Assert.Equal(0, (await recovered.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Revision);
    }

    [Fact]
    public async Task CorruptDurableBytesFailIntegrityBeforeDeserializationAsync()
    {
        using var directory = new RegistryStateDirectory();
        var repository = await SqliteRegistryRepository.OpenAsync(directory.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(directory.Path, "registry.sqlite"), Pooling = false }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        await connection.OpenAsync().ConfigureAwait(true);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE registry_state SET state = X'7B7D' WHERE id=1";
        await command.ExecuteNonQueryAsync().ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await repository.ReadAsync(CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }
}
