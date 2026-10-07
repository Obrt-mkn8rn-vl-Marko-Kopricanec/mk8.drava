using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistrationPublicationSnapshotTests
{
    [Theory]
    [InlineData("svc.site.test", "/", 443, "https://svc.site.test/")]
    [InlineData("edge.site.test", "/api/", 8443, "https://edge.site.test:8443/api/")]
    [InlineData("external.example", "/api/", 0, "http://external.example/api/")]
    public async Task ReadyAndAssignedUrlUseOneGatewayProofSnapshotAsync(string host, string path, int httpsPort, string expected)
    {
        var publication = new OneReadPublication();
        using var fixture = new EnrollmentTestFixture(publication);
        await fixture.InitializeAsync().ConfigureAwait(true);
        var command = EnrollmentTestFixture.Command();
        await fixture.Handler.SubmitAsync(fixture.Sign(command), CancellationToken.None).ConfigureAwait(true);
        publication.Reset(new GatewayPublicationProof(1, fixture.Clock.GetUtcNow().AddMinutes(1), "site.test", 80, httpsPort));
        var intent = fixture.Registry.State.Instances[command.Identity.InstanceId];
        fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30));
        fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(fixture.Registry.State.Revision, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(30)) { Address = new PublishedServiceAddress(host, path) });
        var status = await fixture.Handler.SubmitAsync(fixture.Sign(command with { Operation = RegistrationOperation.Status, Advertisement = null }), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Ready, status.Phase);
#pragma warning disable HLQ005 // xUnit cardinality assertion, not a LINQ Single operation; First would weaken this assertion.
        Assert.Equal(expected, Assert.Single(status.AssignedUrls));
#pragma warning restore HLQ005
        Assert.Equal(1, publication.Reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AbsentExpiredOrAnotherGenerationCannotProduceReadyAsync(int mode)
    {
        var publication = new OneReadPublication();
        using var fixture = new EnrollmentTestFixture(publication);
        await fixture.InitializeAsync().ConfigureAwait(true);
        var command = EnrollmentTestFixture.Command();
        await fixture.Handler.SubmitAsync(fixture.Sign(command), CancellationToken.None).ConfigureAwait(true);
        var intent = fixture.Registry.State.Instances[command.Identity.InstanceId];
        fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30));
        fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(fixture.Registry.State.Revision, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(30)) { Address = new PublishedServiceAddress("svc.site.test", "/") });
        publication.Reset(mode == 0 ? null : new GatewayPublicationProof(mode == 2 ? 2 : 1, fixture.Clock.GetUtcNow().AddSeconds(mode == 1 ? -1 : 30), "site.test", 80, 443));
        var status = await fixture.Handler.SubmitAsync(fixture.Sign(command with { Operation = RegistrationOperation.Status, Advertisement = null }), CancellationToken.None).ConfigureAwait(true);
        Assert.NotEqual(RegistrationPhase.Ready, status.Phase);
        Assert.Empty(status.AssignedUrls);
    }

    [Theory]
    [InlineData("edge.site.test", true)]
    [InlineData("site.test", false)]
    [InlineData("deep.edge.site.test", false)]
    [InlineData("edge.another.test", false)]
    [InlineData("site.test.attacker.example", false)]
    public void AcknowledgedSiteWildcardOnlyCoversOneLabel(string host, bool covered)
    {
        var gateway = new GatewayPublicationProof(1, DateTimeOffset.UtcNow.AddMinutes(1), "site.test", 80, 443);
        Assert.Equal(covered, gateway.CoversCertificateHost(host));
    }

    [Fact]
    public async Task PublicationWithoutAnAddressCannotInventAReadyUrlAsync()
    {
        var publication = new OneReadPublication();
        using var fixture = new EnrollmentTestFixture(publication);
        await fixture.InitializeAsync().ConfigureAwait(true);
        var command = EnrollmentTestFixture.Command();
        await fixture.Handler.SubmitAsync(fixture.Sign(command), CancellationToken.None).ConfigureAwait(true);
        var intent = fixture.Registry.State.Instances[command.Identity.InstanceId];
        fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30));
        fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(fixture.Registry.State.Revision, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(30)));
        Assert.False(fixture.Availability.IsEligible(intent.Identity));
        publication.Reset(new GatewayPublicationProof(1, fixture.Clock.GetUtcNow().AddSeconds(30), "site.test", 80, 443));
        var status = await fixture.Handler.SubmitAsync(fixture.Sign(command with { Operation = RegistrationOperation.Status, Advertisement = null }), CancellationToken.None).ConfigureAwait(true);
        Assert.NotEqual(RegistrationPhase.Ready, status.Phase);
        Assert.Empty(status.AssignedUrls);
    }

    private sealed class OneReadPublication : IGatewayPublicationSource
    {
        private GatewayPublicationProof? _proof;
        public int Reads { get; private set; }
        public void Reset(GatewayPublicationProof? proof) { Reads = 0; _proof = proof; }
        public GatewayPublicationProof? ReadPublicationProof() => ++Reads == 1 ? _proof : null;
    }
}
