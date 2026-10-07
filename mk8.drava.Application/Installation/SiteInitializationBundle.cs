using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Installation;

internal sealed record SiteInitializationBundle(ApplicationBootstrap Application, GatewayBootstrap Gateway, NodeEnrollmentProfile Enrollment)
{
    public static SiteInitializationBundle Create(SiteInitializationOptions options, string fingerprint)
    {
        var destination = options.DestinationDirectory;
        var applicationState = Path.Combine(destination, "application");
        var ipc = new IpcEndpoint
        {
            UnixSocketPath = Path.Combine(applicationState, "app.sock"),
            IdentityTokenPath = Path.Combine(destination, "gateway.token"),
        };
        var controller = new ControllerBootstrap
        {
            Domain = options.Domain, CertificateAuthorityPath = Path.Combine(applicationState, "site-ca.pfx"),
            EnrollmentRootFingerprint = fingerprint, RegistrationPort = options.RegistrationPort,
            PublicAddresses = [options.BindAddress], DnsServerAddress = options.DnsServerAddress, DnsServerPort = options.DnsServerPort,
            Registration = options.Registration, ServingPlan = options.ServingPlan,
        };
        var application = new ApplicationBootstrap
        {
            SiteId = options.SiteId, NodeId = options.NodeId, StateDirectory = applicationState, Listen = ipc,
            IngressAddress = options.BindAddress, HttpPort = options.HttpPort, HttpsPort = options.HttpsPort,
            ManagementPort = options.ManagementPort, AdministratorTokenPath = Path.Combine(applicationState, "administrator.token"), Controller = controller,
        };
        var gateway = new GatewayBootstrap
        {
            SiteId = options.SiteId, BindAddress = options.BindAddress, StateDirectory = Path.Combine(destination, "gateway"),
            Application = ipc, HttpPort = options.HttpPort, HttpsPort = options.HttpsPort, RegistrationPort = options.RegistrationPort,
            ManagementPort = options.ManagementPort, DiscoveryEnabled = options.DiscoveryEnabled, Plan = options.GatewayPlan,
            EnrollmentRootFingerprint = fingerprint,
        };
        var enrollment = new NodeEnrollmentProfile
        {
            Site = new RegistrationSiteTrust
            {
                SiteId = options.SiteId, Domain = options.Domain, RootFingerprint = fingerprint,
                RootCertificatePath = Path.Combine(destination, "node", "site-root.der"),
                NodeCertificatePath = Path.Combine(destination, "node", "node.pfx"),
            },
            NodeId = options.NodeId, OwnerId = options.OwnerId, ServicePrefix = options.ServicePrefix,
            GatewayAddresses = [options.BindAddress], RegistrationPort = options.RegistrationPort, MulticastDiscovery = options.DiscoveryEnabled,
        };
        return new SiteInitializationBundle(application, gateway, enrollment);
    }
}
