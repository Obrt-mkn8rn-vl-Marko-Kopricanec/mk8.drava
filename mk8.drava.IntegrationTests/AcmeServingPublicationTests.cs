using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.DAL.Publication;
using Mk8.Drava.Transport.Certificates;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class AcmeServingPublicationTests
{
    [Fact]
    public async Task RootlessIssuedBundleUsesIndependentApprovedRootAndPublishesAnUnacknowledgedGenerationAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        var role = await fixture.OpenRoleAsync(bootstrap).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(role, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var pending = state.Read("local"); state.Acknowledge(Acknowledgment(pending));
        var issued = WithoutRoot(publicMaterial.Pfx, publicMaterial.Root.RawData);
        await state.PublishIssuedCertificateAsync(issued, CancellationToken.None).ConfigureAwait(true);
        var current = state.Read("local"); Assert.False(current.ServingPending); Assert.Equal(pending.Generation + 1, current.Generation); Assert.Null(state.ReadPublicationProof());
        var persisted = ReadPersistedPlan(role);
        Assert.Equal(current.Certificates[0].Pfx, persisted.Certificates[0].Pfx);
        using var validated = new ValidatedServingPlan(current, fixture.Gateway with { ServingTrust = bootstrap.Controller!.ServingTrust }, fixture.Clock, requireCurrent: true);
        Assert.Equal(publicMaterial.Leaf.RawData, validated.ServingCertificate.RawData); Assert.True(validated.HasServingCertificate);
        Assert.True(state.Acknowledge(Acknowledgment(current))); Assert.NotNull(state.ReadPublicationProof());
        var restoredRole = await fixture.OpenRoleAsync(bootstrap).ConfigureAwait(true);
        await using var restoredRoleLifetime = restoredRole.ConfigureAwait(true);
        using var restored = await ServingPlanState.OpenAsync(restoredRole, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(current.ToByteArray(), restored.Read("local").ToByteArray());
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(bootstrap.StateDirectory, ".env")));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("wrong-root")]
    [InlineData("missing-intermediate")]
    [InlineData("client-leaf")]
    public async Task InvalidIssuedMaterialNeverReplacesProtectedMaterialOrAcceptedPlanAsync(string fault)
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        using var foreign = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        var role = await fixture.OpenRoleAsync(bootstrap).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(role, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        await state.PublishIssuedCertificateAsync(WithoutRoot(publicMaterial.Pfx, publicMaterial.Root.RawData), CancellationToken.None).ConfigureAwait(true);
        var accepted = state.Read("local"); state.Acknowledge(Acknowledgment(accepted));
        var priorMaterial = role.ReadMaterial(); var priorPlan = ReadPersistedPlan(role);
        var invalid = fault switch
        {
            "garbage" => new byte[256],
            "wrong-root" => WithoutRoot(foreign.Pfx, foreign.Root.RawData),
            "missing-intermediate" => publicMaterial.Leaf.Export(X509ContentType.Pkcs12),
            "client-leaf" => publicMaterial.Client.Export(X509ContentType.Pkcs12),
            _ => throw new InvalidOperationException("Unknown test corruption."),
        };
        Exception? rejected = null;
        try { await state.PublishIssuedCertificateAsync(invalid, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception exception) when (exception is InvalidDataException or System.Security.Cryptography.CryptographicException) { rejected = exception; }
        Assert.NotNull(rejected); Assert.Equal(priorMaterial, role.ReadMaterial()); Assert.Equal(priorPlan, ReadPersistedPlan(role));
        Assert.Equal(accepted.ToByteArray(), state.Read("local").ToByteArray()); Assert.True(state.IsAcknowledged);
    }

    [Fact]
    public async Task OwnerRootMismatchAndPreCanceledPublicationNeverCreateServingMaterialAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow()); using var foreign = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        var controller = Assert.IsType<ControllerBootstrap>(bootstrap.Controller);
        bootstrap = bootstrap with { Controller = controller with { Acme = controller.Acme with { PinnedServingRoot = new SecretBytes(foreign.Root.RawData) } } };
        var role = await fixture.OpenRoleAsync(bootstrap).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(role, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var original = state.Read("local"); var originalMaterial = role.ReadMaterial();
        await Assert.ThrowsAsync<InvalidDataException>(() => state.PublishIssuedCertificateAsync(publicMaterial.Pfx, CancellationToken.None).AsTask()).ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.PublishIssuedCertificateAsync(publicMaterial.Pfx, cancellation.Token).AsTask()).ConfigureAwait(true);
        Assert.Equal(originalMaterial, role.ReadMaterial()); Assert.Equal(original.ToByteArray(), state.Read("local").ToByteArray()); Assert.Null(state.ReadPublicationProof());
    }

    private static PresentationPlan ReadPersistedPlan(PortableRole<ApplicationBootstrap> role)
    {
        var bytes = Assert.IsType<byte[]>(new GatewayMaterialStore(role).Read());
        try { return PresentationPlan.Parser.ParseFrom(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static byte[] WithoutRoot(byte[] pfx, byte[] root)
    {
        var certificates = X509CertificateLoader.LoadPkcs12Collection(pfx, password: null, X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            var included = new X509Certificate2Collection();
            foreach (var certificate in certificates) if (!certificate.RawData.AsSpan().SequenceEqual(root)) included.Add(certificate);
            return included.Export(X509ContentType.Pkcs12) ?? throw new InvalidOperationException("Rootless development export failed.");
        }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }

    private static PlanAcknowledgment Acknowledgment(PresentationPlan plan) => new() { Version = 1, GatewayId = plan.GatewayId, Generation = plan.Generation, ContentSha256 = plan.ContentSha256, Applied = true };
}
