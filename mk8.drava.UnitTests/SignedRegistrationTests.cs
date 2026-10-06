using System.Security.Cryptography;
using Google.Protobuf;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Registration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class SignedRegistrationTests
{
    [Fact]
    public async Task SignedRegistrationIsAcceptedWithoutPrematureReadyAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var status = await fixture.Handler.SubmitAsync(fixture.Sign(EnrollmentTestFixture.Command()), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(RegistrationPhase.Checking, status.Phase);
        Assert.Empty(status.AssignedUrls);
        Assert.Equal(2, status.DesiredRevision);
        Assert.Equal(90, status.LeaseSeconds);
    }

    [Fact]
    public async Task SuccessfulSignedCommandCannotBeReplayedAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var signed = fixture.Sign(EnrollmentTestFixture.Command());
        await fixture.Handler.SubmitAsync(signed, CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Handler.SubmitAsync(signed, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(2, fixture.Registry.State.Revision);
    }

    [Fact]
    public async Task SignatureCannotCrossSitesOrModifyTheEndpointAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var command = EnrollmentTestFixture.Command();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Handler.SubmitAsync(fixture.Sign(command, "other-site"), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var signed = fixture.Sign(command);
        signed.JsonPayload = ByteString.CopyFrom(RegistrationJson.Encode(command with { Advertisement = command.Advertisement! with { Port = 12346 } }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Handler.SubmitAsync(signed, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(fixture.Registry.State.Instances);
    }

    [Fact]
    public async Task ValidSignatureCannotChooseAnotherOwnerOrAnUngrantedEndpointAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var command = EnrollmentTestFixture.Command();
        var otherOwner = command with { Identity = command.Identity with { OwnerId = "another-owner" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Handler.SubmitAsync(fixture.Sign(otherOwner), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var otherEndpoint = command with { Advertisement = command.Advertisement! with { Address = "169.254.169.254" } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Handler.SubmitAsync(fixture.Sign(otherEndpoint), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ChallengeExpiresMonotonicallyAndRevocationInvalidatesPendingProofAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var expired = fixture.Sign(EnrollmentTestFixture.Command());
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        fixture.Clock.AdjustUtc(TimeSpan.FromSeconds(-20));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Handler.SubmitAsync(expired, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        var pending = fixture.Sign(EnrollmentTestFixture.Command());
        await fixture.Registry.RevokeNodeAsync("node", "administrator", CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await fixture.Handler.SubmitAsync(pending, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ServingCertificateAndAnUntrustedIssuerCannotEnrollAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        using var serverOnly = EnrollmentTestFixture.CreateLeaf(fixture.Root, fixture.Clock, client: false);
        await fixture.EnrollAsync(serverOnly).ConfigureAwait(true);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Verifier.Authenticate(serverOnly.RawData));
        using var otherRoot = EnrollmentTestFixture.CreateRoot(fixture.Clock);
        using var unknown = EnrollmentTestFixture.CreateLeaf(otherRoot, fixture.Clock, client: true);
        await fixture.EnrollAsync(unknown).ConfigureAwait(true);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Verifier.Authenticate(unknown.RawData));
    }

    [Fact]
    public async Task DurableGrantCannotExtendTheEnrollmentCredentialLifetimeAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.Registry.EnrollAsync(new Mk8.Drava.Application.BLL.Registry.NodeGrant("node", "owner",
            fixture.Leaf.GetCertHashString(HashAlgorithmName.SHA256), "svc", ["127.0.0.1"], 1024, 65535, fixture.Clock.GetUtcNow().AddDays(2), false), "administrator", CancellationToken.None).ConfigureAwait(true);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Verifier.Authenticate(fixture.Leaf.RawData));
    }

    [Fact]
    public async Task ExpiredCertificateCannotCreateAChallengeAsync()
    {
        using var fixture = new EnrollmentTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Verifier.Authenticate(fixture.Leaf.RawData));
    }

    [Theory]
    [InlineData("{\"version\":1,\"version\":1}")]
    [InlineData("{\"version\":1,\"identity\":{\"nodeId\":\"node\",\"nodeId\":\"other\"}}")]
    public void DuplicateRegistrationPropertiesAreRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => RegistrationJson.Decode(System.Text.Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ChallengeQuotaAndSingleUseAreAtomicAcrossConcurrentCallers()
    {
        var clock = new RegistryTimeProvider();
        var challenges = new Mk8.Drava.Application.INF.Registry.EnrollmentChallenges(clock);
        var nonce = challenges.Create(RegistryTestFixture.Fingerprint);
        var successes = 0;
        Parallel.For(0, 100, _ => { if (challenges.Consume(RegistryTestFixture.Fingerprint, nonce)) Interlocked.Increment(ref successes); });
        Assert.Equal(1, successes);
        for (var index = 0; index < 16; index++) challenges.Create(RegistryTestFixture.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => challenges.Create(RegistryTestFixture.Fingerprint));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(32, challenges.Create(RegistryTestFixture.Fingerprint).Length);
    }

    [Fact]
    public void InheritedPolicyNamesRemainCaseInsensitive()
    {
        Assert.Equal(Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection.BalancingAlgorithm.WeightedRoundRobin,
            Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection.UpstreamBalancingPolicy.FromName("ROUND-ROBIN").Algorithm);
    }

    [Fact]
    public void ProofDomainsAreBoundToSiteNonceVersionAndPayload()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var nonce = RandomNumberGenerator.GetBytes(32);
        var signature = RegistrationProof.Sign(key, "site", 1, nonce, "body"u8);
        Assert.True(RegistrationProof.Verify(key, "site", 1, nonce, "body"u8, signature));
        Assert.False(RegistrationProof.Verify(key, "other-site", 1, nonce, "body"u8, signature));
        Assert.False(RegistrationProof.Verify(key, "site", 1, nonce, "other"u8, signature));
        nonce[0] ^= 1;
        Assert.False(RegistrationProof.Verify(key, "site", 1, nonce, "body"u8, signature));
        Assert.Throws<InvalidDataException>(() => RegistrationProof.Digest("site", 2, nonce, "body"u8));
    }
}
