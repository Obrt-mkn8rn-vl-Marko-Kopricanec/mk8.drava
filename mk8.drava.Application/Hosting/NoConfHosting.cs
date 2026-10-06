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
        services.AddSingleton(new NoConfIngressGuard(bootstrap.NodeId, protectedAddresses, [bootstrap.HttpPort, bootstrap.HttpsPort, controller.RegistrationPort]));
        services.AddSingleton<NoConfSnapshotCompiler>();
        services.AddSingleton<IRegisteredReadinessProbe, RegisteredReadinessProbe>();
        services.AddSingleton<IServiceDnsVerifier>(_ => new ServiceDnsVerifier(publicAddresses, controller.DnsServerAddress, controller.DnsServerPort));
        services.AddSingleton<IGatewayPublicationSource>(registration.Plans);
        services.AddSingleton(provider => new NoConfReconciler(registration.Registry,
            provider.GetRequiredService<DestinationAvailabilityStore>(), provider.GetRequiredService<ProxyConfigurationStore>(),
            provider.GetRequiredService<NoConfSnapshotCompiler>(), provider.GetRequiredService<IRegisteredReadinessProbe>(),
            provider.GetRequiredService<IServiceDnsVerifier>(), registration.Plans,
            Path.Combine(bootstrap.StateDirectory, "config", "noconf.json"), controller.Domain, bootstrap.NodeId, TimeProvider.System,
            provider.GetRequiredService<ILogger<NoConfReconciler>>()));
        services.AddHostedService(provider => provider.GetRequiredService<NoConfReconciler>());
    }
}
