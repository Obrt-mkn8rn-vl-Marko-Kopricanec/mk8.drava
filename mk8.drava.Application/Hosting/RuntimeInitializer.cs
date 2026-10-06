using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.INF.Runtime;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal static class RuntimeInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var bootstrap = services.GetRequiredService<ApplicationBootstrap>();
        var result = await services.GetRequiredService<IProxyConfigurationLoader>().LoadAsync(cancellationToken).ConfigureAwait(false);
        if (result is not ProxyConfigurationLoadResult.LoadedResult loaded)
            throw new InvalidDataException("The optional manual configuration is invalid: " + string.Join(" ", result.Errors));
        var options = new List<ListenerOptions>();
        if (bootstrap.HttpPort > 0) options.Add(new ListenerOptions { Name = "http", Address = bootstrap.IngressAddress, Port = bootstrap.HttpPort });
        if (bootstrap.HttpsPort > 0) options.Add(new ListenerOptions { Name = "https", Address = bootstrap.IngressAddress, Port = bootstrap.HttpsPort, Transport = "https", Protocols = "http1,http2" });
        var security = services.GetRequiredService<IProxyAdminSecurityOptionsReader>().Read();
        var urls = bootstrap.ManagementPort > 0 ? new[] { $"https://admin.{bootstrap.Controller!.Domain}:{bootstrap.ManagementPort}" } : [];
        var administrator = new RuntimeAdminSecurityOptions(urls, RequireAuthentication: true, HasConfiguredToken: security.Token is not null, security.Token,
            TokenEnvironmentVariable: "DISABLED", TokenSource: security.Token is null ? "not-configured" : "private-file", RecentAuditCapacity: 1024);
        var snapshot = loaded.Snapshot.WithAdminSecurity(administrator).WithListenersAndRoutes(ProxyConfigurationRuntimeMapper.ToRuntimeListeners(options), loaded.Snapshot.Routes);
        services.GetRequiredService<ProxyConfigurationStore>().Replace(snapshot);
    }
}
