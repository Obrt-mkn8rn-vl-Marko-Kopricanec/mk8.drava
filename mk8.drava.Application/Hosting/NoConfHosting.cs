using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Application.INF.Runtime;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal static class NoConfHosting
{
    public static void AddNoConfRuntime(this IServiceCollection services, ApplicationBootstrap bootstrap, RegistrationRuntime registration)
    {
        var controller = bootstrap.Controller ?? throw new InvalidOperationException("Controller configuration is missing.");
        var publicAddresses = controller.PublicAddresses.Count > 0 ? controller.PublicAddresses : [bootstrap.IngressAddress];
        var protectedAddresses = new List<string>(publicAddresses) { bootstrap.IngressAddress };
        services.AddSingleton(new NoConfIngressGuard(bootstrap.NodeId, protectedAddresses, [bootstrap.HttpPort, bootstrap.HttpsPort, controller.RegistrationPort, bootstrap.ManagementPort]));
        services.AddSingleton<NoConfSnapshotCompiler>();
        services.AddSingleton(registration.Relay);
        var runtimePolicy = RegistrationPolicyMapping.ToPolicy(controller.Registration);
        services.AddSingleton<IRegisteredReadinessProbe>(_ => new RegisteredReadinessProbe(registration.Relay, runtimePolicy));
        if (string.Equals(controller.DnsPublication.Provider, "cloudflare", StringComparison.Ordinal))
        {
            services.AddSingleton<IServiceDnsPublisher>(_ => new CloudflareDnsPublisher(controller.DnsPublication, controller.Domain, bootstrap.SiteId, publicAddresses, TimeProvider.System));
            services.AddSingleton<IServiceDnsVerifier>(provider => new PublishingServiceDnsVerifier(
                new ServiceDnsVerifier(publicAddresses, controller.DnsServerAddress, controller.DnsServerPort), provider.GetRequiredService<IServiceDnsPublisher>()));
        }
        else services.AddSingleton<IServiceDnsVerifier>(_ => new ServiceDnsVerifier(publicAddresses, controller.DnsServerAddress, controller.DnsServerPort));
        services.AddSingleton<IGatewayPublicationSource>(registration.Plans);
        services.AddSingleton(provider => new NoConfReconciler(registration.Registry,
            provider.GetRequiredService<DestinationAvailabilityStore>(), provider.GetRequiredService<ProxyConfigurationStore>(),
            provider.GetRequiredService<NoConfSnapshotCompiler>(), provider.GetRequiredService<IRegisteredReadinessProbe>(),
            provider.GetRequiredService<IServiceDnsVerifier>(), registration.Plans,
            Path.Combine(bootstrap.StateDirectory, "config", "noconf.json"), controller.Domain, bootstrap.NodeId, TimeProvider.System,
            provider.GetRequiredService<ILogger<NoConfReconciler>>(), registration.Policies, runtimePolicy));
        services.AddHostedService(provider => provider.GetRequiredService<NoConfReconciler>());
    }
}
