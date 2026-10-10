using Microsoft.Extensions.DependencyInjection.Extensions;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal static class AcmeHosting
{
    public static void AddOwnerAcmeLifecycle(this IServiceCollection services, ApplicationBootstrap bootstrap, ServingPlanState plans,
        IAcmeCertificateStatusPersistence? history, Mk8.Drava.Application.BLL.Dns.IDnsMutationJournal? journal = null)
    {
        if (bootstrap.Controller?.Acme.Enabled != true) return;
        services.AddSingleton(history ?? throw new InvalidDataException("Enabled ACME requires its certificate status persistence."));
        services.RemoveAll<IAcmeCertificateIssuer>();
        services.RemoveAll<IAcmeRenewalConfigurationSource>();
        services.RemoveAll<IAcmeRenewalScheduleInputSource>();
        services.RemoveAll<IAcmeCertificateMaterialWriter>();
        services.RemoveAll<IAcmeCertificateActivator>();
        services.RemoveAll<IProxyAcmeStatusConfigurationSource>();
        services.RemoveAll<IProxyAcmeCertificateLifecycleStatusSource>();
        services.AddSingleton(new AcmeServingLifecycle(bootstrap, plans));
        services.AddSingleton<IAcmeCertificateIssuer>(_ => new OwnedDns01CertificateIssuer(bootstrap.Controller, bootstrap.SiteId, journal));
        services.AddSingleton<IAcmeRenewalConfigurationSource>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddSingleton<IAcmeRenewalScheduleInputSource>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddSingleton<IAcmeCertificateMaterialWriter>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddSingleton<IAcmeCertificateActivator>(static provider => provider.GetRequiredService<AcmeServingLifecycle>());
        services.AddSingleton(provider => new AcmeOwnerStatusSource(provider.GetRequiredService<AcmeServingLifecycle>(), provider.GetRequiredService<AcmeCertificateStatusStore>(), bootstrap.Controller.Acme.UseStaging));
        services.AddSingleton<IProxyAcmeStatusConfigurationSource>(static provider => provider.GetRequiredService<AcmeOwnerStatusSource>());
        services.AddSingleton<IProxyAcmeCertificateLifecycleStatusSource>(static provider => provider.GetRequiredService<AcmeOwnerStatusSource>());
        services.AddHostedService<AcmeRenewalService>();
    }
}
