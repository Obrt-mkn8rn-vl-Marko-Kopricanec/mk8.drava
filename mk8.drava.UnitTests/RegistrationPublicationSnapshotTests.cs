using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistrationPublicationSnapshotTests
{
    [Fact]
    public async Task ReadyAndAssignedUrlUseOneGatewayProofSnapshotAsync()
    {
        var publication = new OneReadPublication();
        using var fixture = new EnrollmentTestFixture(publication);
        await fixture.InitializeAsync().ConfigureAwait(true);
        var command = EnrollmentTestFixture.Command();
        await fixture.Handler.SubmitAsync(fixture.Sign(command), CancellationToken.None).ConfigureAwait(true);
        publication.Reset(new GatewayPublicationProof(1, fixture.Clock.GetUtcNow().AddMinutes(1), "site.test", 80, 443));
        var intent = fixture.Registry.State.Instances[command.Identity.InstanceId];
        fixture.Availability.SetReadiness(intent.Identity, true, TimeSpan.FromSeconds(30));
        fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(fixture.Registry.State.Revision, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(30)));
        var status = await fixture.Handler.SubmitAsync(fixture.Sign(command with { Operation = RegistrationOperation.Status, Advertisement = null }), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Ready, status.Phase);
#pragma warning disable HLQ005 // xUnit cardinality assertion, not a LINQ Single operation; First would weaken this assertion.
        Assert.Equal("https://svc.site.test/", Assert.Single(status.AssignedUrls));
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
        fixture.Availability.SetPublication(intent.Identity, new DestinationPublication(fixture.Registry.State.Revision, 1, true, true, fixture.Clock.GetUtcNow().AddSeconds(30)));
        publication.Reset(mode == 0 ? null : new GatewayPublicationProof(mode == 2 ? 2 : 1, fixture.Clock.GetUtcNow().AddSeconds(mode == 1 ? -1 : 30), "site.test", 80, 443));
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
