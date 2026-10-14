using Microsoft.Extensions.DependencyInjection;

namespace Mk8.Drava.Registration;

public static class DravaRegistrationExtensions
{
    /// <summary>Loads protected registration settings before adding the SDK's hosted services.</summary>
    public static async Task<IServiceCollection> AddDravaRegistrationAsync(this IServiceCollection services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static service => service.ServiceType == typeof(DravaRegistrationOptions))) throw new InvalidOperationException("A host can register one service identity; replicas use separate hosts.");
        var options = await DravaRegistrationOptions.LoadAsync(cancellationToken).ConfigureAwait(false);
        return services.AddDravaRegistration(options);
    }

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
