using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Installation;

internal static class SiteInitializer
{
    public static async Task<string> InitializeAsync(string optionsPath, CancellationToken cancellationToken)
    {
        var options = await BootstrapFile.LoadAsync<SiteInitializationOptions>(optionsPath, cancellationToken).ConfigureAwait(false);
        options.Validate();
        using var workspace = PrivateSiteWorkspace.Create(options.DestinationDirectory);
        var applicationState = workspace.CreateDirectory("application");
        workspace.CreateDirectory("gateway");
        var nodeState = workspace.CreateDirectory("node");
        var issuerPath = Path.Combine(applicationState, "site-ca.pfx");
        var fingerprint = await LocalSiteCertificateAuthority.InitializeAsync(issuerPath, options.SiteId, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        using var authority = LocalSiteCertificateAuthority.Open(issuerPath, fingerprint, TimeProvider.System);
        using var root = authority.PublicCertificate;
        using var node = authority.IssueNode(options.NodeId, options.EndpointAddresses);
        await PrivateCertificateFile.WriteNewAsync(Path.Combine(nodeState, "node.pfx"), node.Export(X509ContentType.Pkcs12), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync(Path.Combine("node", "site-root.der"), root.RawData, cancellationToken).ConfigureAwait(false);
        await EnrollAsync(applicationState, options, node, cancellationToken).ConfigureAwait(false);
        var bundle = SiteInitializationBundle.Create(options, fingerprint);
        await WriteBundleAsync(workspace, bundle, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        workspace.Publish();
        return fingerprint;
    }

    private static async Task EnrollAsync(string state, SiteInitializationOptions options, X509Certificate2 node, CancellationToken cancellationToken)
    {
        var repository = await SqliteRegistryRepository.OpenAsync(state, options.SiteId, cancellationToken).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        using var registry = new RegistryCoordinator(repository, new DestinationAvailabilityStore(TimeProvider.System), TimeProvider.System);
        await registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var grant = new NodeGrant(options.NodeId, options.OwnerId, node.GetCertHashString(HashAlgorithmName.SHA256), options.ServicePrefix,
            options.EndpointAddresses, options.MinimumPort, options.MaximumPort, new DateTimeOffset(node.NotAfter.ToUniversalTime()), revoked: false);
        await registry.EnrollAsync(grant, options.OwnerId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBundleAsync(PrivateSiteWorkspace workspace, SiteInitializationBundle bundle, CancellationToken cancellationToken)
    {
        await workspace.WriteAsync("gateway.token", Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync(Path.Combine("application", "administrator.token"), Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync("application.json", Encoding.UTF8.GetBytes(BootstrapFile.Serialize(bundle.Application)), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync("gateway.json", Encoding.UTF8.GetBytes(BootstrapFile.Serialize(bundle.Gateway)), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync(Path.Combine("node", "enrollment.json"), Encoding.UTF8.GetBytes(BootstrapFile.Serialize(bundle.Enrollment)), cancellationToken).ConfigureAwait(false);
    }
}
