using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Installation;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.Hosting;

namespace Mk8.Drava.Application.Installation;

internal static class SiteInitializer
{
    public static async Task<string> InitializeAsync(string optionsPath, CancellationToken cancellationToken)
    {
        using var startup = new ApplicationStartupProgress();
        startup.Enter(ApplicationStartupPhase.BootstrapRead);
        var options = await BootstrapFile.LoadAsync<SiteInitializationOptions>(optionsPath, cancellationToken).ConfigureAwait(false);
        options.Validate();
        startup.Enter(ApplicationStartupPhase.SiteWorkspaceOpen);
        using var workspace = PrivateSiteWorkspace.Create(options.DestinationDirectory);
        var applicationState = workspace.CreateDirectory("application");
        workspace.CreateDirectory("gateway");
        workspace.CreateDirectory("node");
        startup.Enter(ApplicationStartupPhase.SiteIssuerCreate);
        using var authority = LocalSiteCertificateAuthority.Create(options.SiteId, TimeProvider.System);
        var fingerprint = authority.Fingerprint;
        using var root = authority.PublicCertificate;
        using var node = authority.IssueNode(options.NodeId, options.EndpointAddresses);
        var bundle = SiteInitializationBundle.Create(options, fingerprint, new SecretText(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))), new SecretText(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))), authority.PrivateMaterial, new SecretBytes(root.RawData), PortableCertificate.ExportPrivate(node));
        bundle.Application.Validate(); bundle.Gateway.Validate(); bundle.Enrollment.Validate();
        startup.Enter(ApplicationStartupPhase.SiteConfigurationProtect);
        await WriteBundleAsync(workspace, bundle, cancellationToken).ConfigureAwait(false);
        startup.Enter(ApplicationStartupPhase.SiteNodeEnroll);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(Path.Combine(workspace.StagingDirectory, "roles", "application", "Environment", ".env"), cancellationToken).ConfigureAwait(false);
        await using (role.ConfigureAwait(false))
        {
            await EnrollAsync(applicationState, options, node, new PortablePolicyPayloadStore(role), cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        startup.Enter(ApplicationStartupPhase.SitePublish);
        workspace.Publish();
        startup.Enter(ApplicationStartupPhase.SiteInitializationComplete);
        return fingerprint;
    }

    private static async Task EnrollAsync(string state, SiteInitializationOptions options, X509Certificate2 node, IPolicyPayloadStore payloads, CancellationToken cancellationToken)
    {
        var repository = await SqliteRegistryRepository.OpenAsync(state, options.SiteId, payloads, cancellationToken).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        using var registry = new RegistryCoordinator(repository, new DestinationAvailabilityStore(TimeProvider.System), TimeProvider.System);
        await registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var grant = new NodeGrant(options.NodeId, options.OwnerId, node.GetCertHashString(HashAlgorithmName.SHA256), options.ServicePrefix,
            options.EndpointAddresses, options.MinimumPort, options.MaximumPort, new DateTimeOffset(node.NotAfter.ToUniversalTime()), revoked: false);
        await registry.EnrollAsync(grant, options.OwnerId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBundleAsync(PrivateSiteWorkspace workspace, SiteInitializationBundle bundle, CancellationToken cancellationToken)
    {
        workspace.CreateDirectory(Path.Combine("roles", "application", "Environment"));
        workspace.CreateDirectory(Path.Combine("roles", "gateway", "Environment"));
        workspace.CreateDirectory(Path.Combine("roles", "node", "Environment"));
        await workspace.WriteAsync(Path.Combine("roles", "application", "Environment", ".env"), Encoding.UTF8.GetBytes(PortableConfiguration.Serialize(bundle.Application)), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync(Path.Combine("roles", "gateway", "Environment", ".env"), Encoding.UTF8.GetBytes(PortableConfiguration.Serialize(bundle.Gateway)), cancellationToken).ConfigureAwait(false);
        await workspace.WriteAsync(Path.Combine("roles", "node", "Environment", ".env"), Encoding.UTF8.GetBytes(PortableConfiguration.Serialize(bundle.Enrollment)), cancellationToken).ConfigureAwait(false);
        _ = await PortableConfiguration.LoadDocumentAsync<ApplicationBootstrap>(Path.Combine(workspace.StagingDirectory, "roles", "application", "Environment", ".env"), cancellationToken).ConfigureAwait(false);
        _ = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(Path.Combine(workspace.StagingDirectory, "roles", "gateway", "Environment", ".env"), cancellationToken).ConfigureAwait(false);
        _ = await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(Path.Combine(workspace.StagingDirectory, "roles", "node", "Environment", ".env"), cancellationToken).ConfigureAwait(false);
    }
}
