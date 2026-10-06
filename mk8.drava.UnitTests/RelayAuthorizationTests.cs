using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Contracts.Relay.V1;
using Mk8.Drava.Transport.Relay;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RelayAuthorizationTests
{
    [Fact]
    public void ExactLiveMappingAndControllerRoleAuthorizeOneConnection()
    {
        using var fixture = new RelayTestFixture();
        var capability = fixture.Capability();
        var approved = fixture.Authorize(capability);
        Assert.Equal(fixture.Command.Identity.InstanceId, approved.Endpoint.Identity.InstanceId);
        Assert.Equal("127.0.0.1", approved.Endpoint.Address);
        Assert.Equal(12345, approved.Endpoint.Port);
        Assert.Same(capability.Identity, fixture.Command.Identity);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(capability));
    }

    [Fact]
    public void NodeLeafAndDifferentControllerPeerCannotAuthorizeTheRelay()
    {
        using var fixture = new RelayTestFixture();
        using var node = EnrollmentTestFixture.CreateLeaf(fixture.Root, fixture.Clock, client: true);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(), node));
        using var otherController = RelayTestFixture.CreateController(fixture.Root, fixture.Clock, "site", fixture.ControllerEpoch);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(), peer: otherController));
        Assert.Equal(12345, fixture.Authorize(fixture.Capability()).Endpoint.Port);
    }

    [Fact]
    public void WrongSiteWriterEpochAndTrustRootAreRejected()
    {
        using var fixture = new RelayTestFixture();
        using var otherSite = RelayTestFixture.CreateController(fixture.Root, fixture.Clock, "other-site", fixture.ControllerEpoch);
        using var otherEpoch = RelayTestFixture.CreateController(fixture.Root, fixture.Clock, "site", Guid.NewGuid().ToString("N"));
        using var otherRoot = EnrollmentTestFixture.CreateRoot(fixture.Clock);
        using var untrusted = RelayTestFixture.CreateController(otherRoot, fixture.Clock, "site", fixture.ControllerEpoch);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(), otherSite));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(), otherEpoch));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(), untrusted));
    }

    [Fact]
    public void EndpointOwnerContractAndBootSubstitutionAreRejected()
    {
        using var fixture = new RelayTestFixture();
        foreach (var identity in new[] { fixture.Command.Identity with { OwnerId = "other" }, fixture.Command.Identity with { ContractId = "v2" }, fixture.Command.Identity with { BootId = Guid.NewGuid().ToString("N") } })
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(identity: identity)));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(advertisement: fixture.Command.Advertisement! with { Port = 12346 })));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(advertisement: fixture.Command.Advertisement! with { Address = "192.0.2.10" })));
    }

    [Fact]
    public void LocalMappingRequiresGrantedAndActuallyLocalEndpoints()
    {
        using var fixture = new RelayTestFixture();
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Mappings.Renew(fixture.Command.Identity, fixture.Command.Advertisement! with { Address = "192.0.2.10" }));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Mappings.Renew(fixture.Command.Identity, fixture.Command.Advertisement! with { Address = "169.254.169.254" }));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Mappings.Renew(fixture.Command.Identity, fixture.Command.Advertisement! with { Port = 443 }));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Mappings.Renew(fixture.Command.Identity, fixture.Command.Advertisement! with { Port = 12346 }));
    }

    [Fact]
    public void DrainRevokeAgentRestartAndMappingExpiryRejectNewConnections()
    {
        using var fixture = new RelayTestFixture();
        var capability = fixture.Capability();
        fixture.Mappings.Drain(fixture.Command.Identity);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(capability));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Mappings.Renew(fixture.Command.Identity, fixture.Command.Advertisement!));
        var freshIdentity = fixture.Command.Identity with { BootId = Guid.NewGuid().ToString("N") };
        fixture.Mappings.Renew(freshIdentity, fixture.Command.Advertisement!);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(identity: freshIdentity) with { AgentBootId = Guid.NewGuid().ToString("N") }));
        fixture.Clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(identity: freshIdentity)));
        fixture.Mappings.Renew(freshIdentity, fixture.Command.Advertisement!);
        fixture.Mappings.Revoke();
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(fixture.Capability(identity: freshIdentity)));
    }

    [Fact]
    public void ClockRollbackCannotRestoreAnExpiredOrConsumedCapability()
    {
        using var fixture = new RelayTestFixture();
        var consumed = fixture.Capability();
        var unused = fixture.Capability();
        fixture.Authorize(consumed);
        fixture.Clock.Advance(TimeSpan.FromSeconds(17));
        fixture.Clock.AdjustUtc(TimeSpan.FromSeconds(-17));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(consumed));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Authorize(unused));
    }

    [Fact]
    public void ConcurrentReplayHasExactlyOneWinner()
    {
        using var fixture = new RelayTestFixture();
        var capability = fixture.Capability();
        var accepted = 0;
        Parallel.For(0, 64, _ =>
        {
            try { fixture.Authorize(capability); Interlocked.Increment(ref accepted); }
            catch (UnauthorizedAccessException) { }
        });
        Assert.Equal(1, accepted);
    }

    [Fact]
    public void ProofCannotCrossSitesPayloadsOrRegistrationDomains()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var proof = RelayCapabilityProof.Sign(key, "site", "payload"u8);
        Assert.True(RelayCapabilityProof.Verify(key, "site", "payload"u8, proof));
        Assert.False(RelayCapabilityProof.Verify(key, "other", "payload"u8, proof));
        Assert.False(RelayCapabilityProof.Verify(key, "site", "changed"u8, proof));
        var registration = Mk8.Drava.Transport.Registration.RegistrationProof.Sign(key, "site", 1, RandomNumberGenerator.GetBytes(32), "payload"u8);
        Assert.False(RelayCapabilityProof.Verify(key, "site", "payload"u8, registration));
    }

    [Theory]
    [InlineData("{\"version\":1,\"version\":1}")]
    [InlineData("{\"identity\":{\"siteId\":\"site\",\"siteId\":\"other\"}}")]
    public void AmbiguousOrUnrecognizedCapabilityJsonIsRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => RelayCapabilityJson.Decode(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("{\"unknown\":true}")]
    [InlineData("{\"identity\":null}")]
    public void UnknownAndNullCapabilityPropertiesAreRejected(string json) =>
        Assert.Throws<JsonException>(() => RelayCapabilityJson.Decode(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public async Task ProductionIssuerSeparatesControllerAndNodeRolesAsync()
    {
        using var directory = new RegistryStateDirectory();
        var clock = new RegistryTimeProvider();
        var path = Path.Combine(directory.Path, "issuer.pfx");
        var pin = await LocalSiteCertificateAuthority.InitializeAsync(path, "site", clock, CancellationToken.None).ConfigureAwait(true);
        using var authority = LocalSiteCertificateAuthority.Open(path, pin, clock);
        using var root = authority.PublicCertificate;
        var epoch = Guid.NewGuid().ToString("N");
        using var controller = authority.IssueController("site", epoch);
        ControllerCertificateRole.Validate(controller, root, "site", epoch, clock);
        using var namedController = authority.IssueNode("controller", ["127.0.0.1"]);
        Assert.Throws<UnauthorizedAccessException>(() => ControllerCertificateRole.Validate(namedController, root, "site", epoch, clock));
        Assert.Throws<UnauthorizedAccessException>(() => ControllerCertificateRole.Validate(controller, root, "site", Guid.NewGuid().ToString("N"), clock));
    }

    [Fact]
    public void UnknownPurposesExcessiveReadinessAndTicketLifetimesAreRejected()
    {
        using var fixture = new RelayTestFixture();
        var capability = fixture.Capability();
        Assert.Throws<InvalidDataException>(() => RelayCapabilityJson.Encode(capability with { Purpose = RelayPurpose.Unspecified }));
        Assert.Throws<InvalidDataException>(() => RelayCapabilityJson.Encode(capability with { Purpose = RelayPurpose.Readiness, MaximumDurationSeconds = 60 }));
        Assert.Throws<InvalidDataException>(() => RelayCapabilityJson.Encode(capability with { ExpiresAtUnixMilliseconds = capability.IssuedAtUnixMilliseconds + 15_001 }));
        Assert.Throws<InvalidDataException>(() => RelayCapabilityJson.Encode(capability with { AgentBootId = "unknown" }));
    }
}
