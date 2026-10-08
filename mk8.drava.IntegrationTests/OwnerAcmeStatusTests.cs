using Microsoft.Extensions.DependencyInjection;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.Hosting;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class OwnerAcmeStatusTests
{
    private static readonly string[] Domains = ["site.test", "*.site.test"];
    [Fact]
    public async Task OwnerPendingStatusIsAvailableWithoutLegacyProxyConfigurationOrIssuerConstructionAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, material);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(bootstrap.StateDirectory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var services = AcmeHostedPipelineTests.Services(bootstrap);
        services.AddOwnerAcmeLifecycle(bootstrap, plans, new SqliteAcmeCertificateStatusPersistence(repository, bootstrap.Controller!.Domain, bootstrap.Controller.Acme.DirectoryUrl));
        var provider = services.BuildServiceProvider(); await using var providerLifetime = provider.ConfigureAwait(true);
        var status = provider.GetRequiredService<ProxyAcmeAdministrationService>().GetStatus();
        Assert.NotNull(status); Assert.True(status.Enabled); Assert.Equal(bootstrap.Controller.Acme.DirectoryUrl.AbsoluteUri, status.DirectoryUrl);
        Assert.Collection(status.Certificates, certificate => { Assert.Equal("site", certificate.CertificateId); Assert.False(certificate.Active); Assert.Equal(Domains, certificate.Domains); });
        Assert.False(File.Exists(bootstrap.Controller.Acme.AccountKeyPath)); Assert.False(File.Exists(bootstrap.Controller.Acme.CleanupJournalPath + ".lock"));
        Assert.Null(plans.ReadPublicationProof());
    }

    [Fact]
    public async Task FreshAcceptedMaterialSuppliesAccurateDatesAndAdaptiveDueTimeBeforeFirstRenewalCheckAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, material);
        await File.WriteAllBytesAsync(bootstrap.Controller!.Acme.PinnedServingRootPath, material.Root.RawData).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(bootstrap.Controller.Acme.PinnedServingRootPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var plans = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        await plans.PublishIssuedCertificateAsync(material.Pfx, CancellationToken.None).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(bootstrap.StateDirectory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var repositoryLifetime = repository.ConfigureAwait(true);
        var services = AcmeHostedPipelineTests.Services(bootstrap);
        services.AddOwnerAcmeLifecycle(bootstrap, plans, new SqliteAcmeCertificateStatusPersistence(repository, bootstrap.Controller.Domain, bootstrap.Controller.Acme.DirectoryUrl));
        var provider = services.BuildServiceProvider(); await using var providerLifetime = provider.ConfigureAwait(true);
        var status = provider.GetRequiredService<ProxyAcmeAdministrationService>().GetStatus(); Assert.NotNull(status);
        Assert.Collection(status.Certificates, certificate =>
        {
            Assert.True(certificate.Active); Assert.Equal("acme", certificate.Source);
            var before = new DateTimeOffset(material.Leaf.NotBefore.ToUniversalTime()); var after = new DateTimeOffset(material.Leaf.NotAfter.ToUniversalTime());
            Assert.Equal(before, certificate.NotBeforeUtc); Assert.Equal(after, certificate.NotAfterUtc);
            Assert.Equal(after.Subtract(TimeSpan.FromTicks((after - before).Ticks / 3)), certificate.RenewalDueAtUtc);
        });
        Assert.Null(plans.ReadPublicationProof());
    }
}
