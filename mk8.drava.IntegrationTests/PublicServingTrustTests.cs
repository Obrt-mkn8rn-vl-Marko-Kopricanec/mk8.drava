using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.DAL.Publication;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Certificates;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class PublicServingTrustTests
{
    [Fact]
    public async Task SeparatelyPinnedServingChainPreservesPrivateEnrollmentTrustAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var stateRole = await fixture.OpenRoleAsync().ConfigureAwait(true);
        await using var stateRoleLifetime = stateRole.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(stateRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = WithPublicServing(state.Read("local"), material);
        var json = JsonNode.Parse(BootstrapFile.Serialize(fixture.Gateway))!.AsObject();
        json["servingTrust"] = new JsonObject { ["mode"] = "pinned", ["rootFingerprint"] = material.Root.GetCertHashString(HashAlgorithmName.SHA256) };
        var path = Path.Combine(fixture.Application.StateDirectory, "public-gateway.json");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.SerializeDocument<GatewayBootstrap>(json.ToJsonString())).ConfigureAwait(true);
        var gateway = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        using var validated = new ValidatedServingPlan(plan, gateway, fixture.Clock, requireCurrent: true);
        Assert.Equal(material.Leaf.GetCertHashString(HashAlgorithmName.SHA256), validated.ServingCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        using var privateNode = fixture.Authority.IssueNode("node", ["127.0.0.1"]);
        Assert.True(validated.ValidateClientCertificate(privateNode));
        using var publicClientChain = new X509Chain();
        publicClientChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        publicClientChain.ChainPolicy.CustomTrustStore.Add(material.Root);
        foreach (var intermediate in validated.ServingContext.IntermediateCertificates) publicClientChain.ChainPolicy.ExtraStore.Add(intermediate);
        publicClientChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        publicClientChain.ChainPolicy.DisableCertificateDownloads = true;
        publicClientChain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        Assert.True(publicClientChain.Build(material.Client));
        Assert.False(validated.ValidateClientCertificate(material.Client));
        Assert.Equal(fixture.Gateway.EnrollmentRootFingerprint, gateway.EnrollmentRootFingerprint);
    }

    [Fact]
    public async Task DefaultPrivateServingTrustRejectsASeparatePublicIssuerAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var stateRole = await fixture.OpenRoleAsync().ConfigureAwait(true);
        await using var stateRoleLifetime = stateRole.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(stateRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = WithPublicServing(state.Read("local"), material);
        Assert.Throws<InvalidDataException>(() => new ValidatedServingPlan(plan, fixture.Gateway, fixture.Clock, requireCurrent: true));
    }

    [Theory]
    [InlineData("site-ca", "AB")]
    [InlineData("system", "AB")]
    [InlineData("pinned", "")]
    [InlineData("pinned", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("unknown", "")]
    public void ServingTrustRejectsAmbiguousOrNoncanonicalPolicies(string mode, string fingerprint) =>
        Assert.Throws<InvalidDataException>(() => new ServingTrustSettings { Mode = mode, RootFingerprint = fingerprint }.Validate());

    [Theory]
    [InlineData("site-ca-disguise")]
    [InlineData("wrong-pin")]
    [InlineData("system")]
    [InlineData("legacy")]
    [InlineData("client-leaf")]
    [InlineData("missing-intermediate")]
    [InlineData("missing-root")]
    public async Task OwnerPolicyAndCompleteServerChainAreRequiredAsync(string corruption)
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var stateRole = await fixture.OpenRoleAsync().ConfigureAwait(true);
        await using var stateRoleLifetime = stateRole.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(stateRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = WithPublicServing(state.Read("local"), material);
        var trust = PublicTrust(material);
        switch (corruption)
        {
            case "site-ca-disguise": trust = new ServingTrustSettings(); plan.ServingTrustMode = "site-ca"; plan.ServingRootFingerprint = ""; break;
            case "wrong-pin": trust = trust with { RootFingerprint = new string('0', 64) }; plan.ServingRootFingerprint = trust.RootFingerprint; break;
            case "system": trust = new ServingTrustSettings { Mode = "system" }; plan.ServingTrustMode = "system"; plan.ServingRootFingerprint = ""; break;
            case "legacy": plan.Certificates.RemoveAt(1); break;
            case "client-leaf": plan.Certificates[0].Pfx = ByteString.CopyFrom(material.Client.Export(X509ContentType.Pkcs12)); break;
            case "missing-intermediate": plan.Certificates[0].Pfx = ByteString.CopyFrom(new X509Certificate2Collection { material.Leaf, material.Root }.Export(X509ContentType.Pkcs12)!); break;
            case "missing-root": plan.Certificates[0].Pfx = ByteString.CopyFrom(material.Leaf.Export(X509ContentType.Pkcs12)); break;
            default: throw new InvalidOperationException("Unknown chain corruption.");
        }
        plan.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(plan));
        Assert.Throws<InvalidDataException>(() => new ValidatedServingPlan(plan, fixture.Gateway with { ServingTrust = trust }, fixture.Clock, requireCurrent: true));
    }

    [Fact]
    public async Task PrivateToPublicMigrationRequiresANewAcknowledgmentAndPreservesEnrollmentRootAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var initialRole = await fixture.OpenRoleAsync().ConfigureAwait(true);
        await using var initialRoleLifetime = initialRole.ConfigureAwait(true);
        using var initial = await ServingPlanState.OpenAsync(initialRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var prior = initial.Read("local");
        var application = PublicApplication(fixture, publicMaterial);
        var migratedRole = await fixture.OpenRoleAsync(application).ConfigureAwait(true);
        await using var migratedRoleLifetime = migratedRole.ConfigureAwait(true);
        using var migrated = await ServingPlanState.OpenAsync(migratedRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = migrated.Read("local");
        Assert.Equal(prior.Generation + 1, plan.Generation);
        Assert.Equal(prior.EnrollmentCaDer, plan.EnrollmentCaDer);
        Assert.Equal(publicMaterial.Pfx, plan.Certificates[0].Pfx.ToByteArray());
        Assert.False(migrated.IsAcknowledged);
        Assert.Throws<InvalidDataException>(() => migrated.Acknowledge(Acknowledgment(prior)));
        Assert.True(migrated.Acknowledge(Acknowledgment(plan)));
        using var validated = new ValidatedServingPlan(plan, fixture.Gateway with { ServingTrust = PublicTrust(publicMaterial) }, fixture.Clock, requireCurrent: true);
        Assert.NotEmpty(validated.ServingContext.IntermediateCertificates);
        var restoredRole = await fixture.OpenRoleAsync(application).ConfigureAwait(true);
        await using var restoredRoleLifetime = restoredRole.ConfigureAwait(true);
        using var restored = await ServingPlanState.OpenAsync(restoredRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(plan.ToByteArray(), restored.Read("local").ToByteArray());
        Assert.False(restored.IsAcknowledged);
        var revertedRole = await fixture.OpenRoleAsync().ConfigureAwait(true);
        await using var revertedRoleLifetime = revertedRole.ConfigureAwait(true);
        using var reverted = await ServingPlanState.OpenAsync(revertedRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(plan.Generation + 1, reverted.Read("local").Generation);
        Assert.Equal(prior.EnrollmentCaDer, reverted.Read("local").EnrollmentCaDer);
    }

    [Fact]
    public async Task InvalidPublicReplacementPreservesTheLastAcceptedPlanAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var application = PublicApplication(fixture, material);
        var stateRole = await fixture.OpenRoleAsync(application).ConfigureAwait(true);
        await using var stateRoleLifetime = stateRole.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(stateRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var prior = state.Read("local");
        Assert.True(state.Acknowledge(Acknowledgment(prior)));
        var invalid = application with { Controller = application.Controller! with { ServingCertificate = new SecretBytes(material.Client.Export(X509ContentType.Pkcs12)) } };
        var invalidRole = await fixture.OpenRoleAsync(invalid).ConfigureAwait(true);
        await using var invalidRoleLifetime = invalidRole.ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => ServingPlanState.OpenAsync(invalidRole, fixture.Authority, fixture.Clock, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Equal(prior.ToByteArray(), state.Read("local").ToByteArray());
        Assert.Equal(prior.ToByteArray(), new GatewayMaterialStore(invalidRole).Read());
        Assert.True(state.IsAcknowledged);
        var restoredRole = await fixture.OpenRoleAsync(application).ConfigureAwait(true);
        await using var restoredRoleLifetime = restoredRole.ConfigureAwait(true);
        using var restored = await ServingPlanState.OpenAsync(restoredRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(prior.ToByteArray(), restored.Read("local").ToByteArray());
        Assert.False(restored.IsAcknowledged);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicServerPurposeAcceptsAdditionalUsageWithoutGrantingPrivateEnrollmentAsync(bool includeClientUsage)
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var material = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow(), includeClientUsage);
        var stateRole = await fixture.OpenRoleAsync().ConfigureAwait(true);
        await using var stateRoleLifetime = stateRole.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(stateRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var plan = WithPublicServing(state.Read("local"), material);
        using var validated = new ValidatedServingPlan(plan, fixture.Gateway with { ServingTrust = PublicTrust(material) }, fixture.Clock, requireCurrent: true);
        Assert.Equal(material.Leaf.GetCertHashString(HashAlgorithmName.SHA256), validated.ServingCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.False(validated.ValidateClientCertificate(material.Leaf));
        if (includeClientUsage)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(material.Root);
            foreach (var intermediate in validated.ServingContext.IntermediateCertificates) chain.ChainPolicy.ExtraStore.Add(intermediate);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            Assert.True(chain.Build(material.Leaf));
        }
    }

    private static ServingTrustSettings PublicTrust(DevelopmentPublicServingCertificate material) =>
        new() { Mode = "pinned", RootFingerprint = material.Root.GetCertHashString(HashAlgorithmName.SHA256) };

    private static ApplicationBootstrap PublicApplication(DevelopmentServingPlanFixture fixture, DevelopmentPublicServingCertificate material) =>
        fixture.Application with { Controller = fixture.Application.Controller! with { ServingTrust = PublicTrust(material), ServingCertificate = new SecretBytes(material.Pfx) } };

    private static PlanAcknowledgment Acknowledgment(PresentationPlan plan) =>
        new() { Version = 1, GatewayId = plan.GatewayId, Generation = plan.Generation, ContentSha256 = plan.ContentSha256, Applied = true };

    private static PresentationPlan WithPublicServing(PresentationPlan plan, DevelopmentPublicServingCertificate material)
    {
        plan.ServingTrustMode = "pinned";
        plan.ServingRootFingerprint = material.Root.GetCertHashString(HashAlgorithmName.SHA256);
        plan.Certificates[0].Pfx = ByteString.CopyFrom(material.Pfx);
        plan.Certificates[0].NotAfterUnixSeconds = new DateTimeOffset(material.Leaf.NotAfter.ToUniversalTime()).ToUnixTimeSeconds();
        plan.ValidUntilUnixSeconds = Math.Min(plan.ValidUntilUnixSeconds, plan.Certificates[0].NotAfterUnixSeconds);
        plan.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(plan));
        return plan;
    }
}
