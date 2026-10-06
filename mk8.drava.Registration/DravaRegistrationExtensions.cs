using Microsoft.Extensions.DependencyInjection;

namespace Mk8.Drava.Registration;

public static class DravaRegistrationExtensions
{
    public static IServiceCollection AddDravaRegistration(this IServiceCollection services, DravaRegistrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(static service => service.ServiceType == typeof(DravaRegistrationOptions))) throw new InvalidOperationException("A host can register one service identity; replicas use separate hosts.");
        services.AddSingleton(options.CopyValidated());
        services.AddSingleton<DravaRegistrationState>();
        services.AddHostedService<DravaRegistrationHostedService>();
        return services;
    }
}
