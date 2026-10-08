using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.DAL.Acme;
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
        await WriteRootAsync(bootstrap.Controller!.Acme.PinnedServingRootPath, publicMaterial.Root.RawData).ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var pending = state.Read("local"); state.Acknowledge(Acknowledgment(pending));
        var issued = WithoutRoot(publicMaterial.Pfx, publicMaterial.Root.RawData);
        await state.PublishIssuedCertificateAsync(issued, CancellationToken.None).ConfigureAwait(true);
        var current = state.Read("local"); Assert.False(current.ServingPending); Assert.Equal(pending.Generation + 1, current.Generation); Assert.Null(state.ReadPublicationProof());
        Assert.Equal(current.Certificates[0].Pfx.ToByteArray(), PrivateCertificateFile.Read(bootstrap.Controller.ServingCertificatePath));
        using var validated = new ValidatedServingPlan(current, fixture.Gateway with { ServingTrust = bootstrap.Controller.ServingTrust }, fixture.Clock, requireCurrent: true);
        Assert.Equal(publicMaterial.Leaf.RawData, validated.ServingCertificate.RawData); Assert.True(validated.HasServingCertificate);
        Assert.True(state.Acknowledge(Acknowledgment(current))); Assert.NotNull(state.ReadPublicationProof());
        using var restored = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(current.ToByteArray(), restored.Read("local").ToByteArray());
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(bootstrap.Controller.ServingCertificatePath));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("wrong-root")]
    [InlineData("missing-intermediate")]
    [InlineData("client-leaf")]
    public async Task InvalidIssuedMaterialNeverReplacesPrivateFilesOrAcceptedPlanAsync(string fault)
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        using var foreign = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        await WriteRootAsync(bootstrap.Controller!.Acme.PinnedServingRootPath, publicMaterial.Root.RawData).ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        await state.PublishIssuedCertificateAsync(WithoutRoot(publicMaterial.Pfx, publicMaterial.Root.RawData), CancellationToken.None).ConfigureAwait(true);
        var accepted = state.Read("local"); state.Acknowledge(Acknowledgment(accepted));
        var priorFile = PrivateCertificateFile.Read(bootstrap.Controller.ServingCertificatePath); var priorPlan = GatewayMaterialStore.Read(bootstrap.StateDirectory);
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
        Assert.NotNull(rejected); Assert.Equal(priorFile, PrivateCertificateFile.Read(bootstrap.Controller.ServingCertificatePath)); Assert.Equal(priorPlan, GatewayMaterialStore.Read(bootstrap.StateDirectory));
        Assert.Equal(accepted.ToByteArray(), state.Read("local").ToByteArray()); Assert.True(state.IsAcknowledged);
    }

    [Fact]
    public async Task OwnerRootMismatchAndPreCanceledPublicationNeverCreateServingMaterialAsync()
    {
        using var fixture = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var publicMaterial = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow()); using var foreign = DevelopmentPublicServingCertificate.Create(fixture.Clock.GetUtcNow());
        var bootstrap = PendingServingPlanTests.PublicApplication(fixture, publicMaterial);
        await WriteRootAsync(bootstrap.Controller!.Acme.PinnedServingRootPath, foreign.Root.RawData).ConfigureAwait(true);
        using var state = await ServingPlanState.OpenAsync(bootstrap, fixture.Authority, fixture.Clock, CancellationToken.None).ConfigureAwait(true);
        var original = state.Read("local");
        await Assert.ThrowsAsync<InvalidDataException>(() => state.PublishIssuedCertificateAsync(publicMaterial.Pfx, CancellationToken.None).AsTask()).ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.PublishIssuedCertificateAsync(publicMaterial.Pfx, cancellation.Token).AsTask()).ConfigureAwait(true);
        Assert.True(PrivateCertificateFile.IsAbsent(bootstrap.Controller.ServingCertificatePath)); Assert.Equal(original.ToByteArray(), state.Read("local").ToByteArray()); Assert.Null(state.ReadPublicationProof());
    }

    private static byte[] WithoutRoot(byte[] pfx, byte[] root)
    {
        var certificates = X509CertificateLoader.LoadPkcs12Collection(pfx, null, X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            var included = new X509Certificate2Collection();
            foreach (var certificate in certificates) if (!certificate.RawData.AsSpan().SequenceEqual(root)) included.Add(certificate);
            return included.Export(X509ContentType.Pkcs12) ?? throw new InvalidOperationException("Rootless development export failed.");
        }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }

    private static async Task WriteRootAsync(string path, byte[] bytes)
    {
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    private static PlanAcknowledgment Acknowledgment(PresentationPlan plan) => new() { Version = 1, GatewayId = plan.GatewayId, Generation = plan.Generation, ContentSha256 = plan.ContentSha256, Applied = true };
}
