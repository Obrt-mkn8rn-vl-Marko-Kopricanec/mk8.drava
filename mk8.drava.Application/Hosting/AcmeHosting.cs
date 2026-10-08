using Microsoft.Extensions.DependencyInjection.Extensions;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal static class AcmeHosting
{
    public static void AddOwnerAcmeLifecycle(this IServiceCollection services, ApplicationBootstrap bootstrap, ServingPlanState plans,
        IAcmeCertificateStatusPersistence history)
    {
        if (bootstrap.Controller?.Acme.Enabled != true) return;
        services.AddSingleton(history);
        services.RemoveAll<IAcmeCertificateIssuer>();
        services.RemoveAll<IAcmeRenewalConfigurationSource>();
        services.RemoveAll<IAcmeRenewalScheduleInputSource>();
        services.RemoveAll<IAcmeCertificateMaterialWriter>();
        services.RemoveAll<IAcmeCertificateActivator>();
        services.AddSingleton(new AcmeServingLifecycle(bootstrap, plans));
        services.AddSingleton<IAcmeCertificateIssuer>(_ => new OwnedDns01CertificateIssuer(bootstrap.Controller, bootstrap.SiteId));
        services.AddSingleton<IAcmeRenewalConfigurationSource>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddSingleton<IAcmeRenewalScheduleInputSource>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddSingleton<IAcmeCertificateMaterialWriter>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddSingleton<IAcmeCertificateActivator>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddHostedService<AcmeRenewalService>();
    }
}
