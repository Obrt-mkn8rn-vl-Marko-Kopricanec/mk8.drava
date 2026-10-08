using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.INF.Administration;
using Mk8.Drava.Application.INF.Runtime;
using Mk8.Drava.Application.Transport;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.IntegrationTests;

internal static class DevelopmentControlPlanHost
{
    public static WebApplication Build(ApplicationBootstrap bootstrap, ServingPlanState plans)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration["Mdrava:DataDirectory"] = bootstrap.StateDirectory;
        builder.Services.AddProxyApplication(builder.Configuration);
        builder.Services.AddSingleton<IProxyListenerReloadApplier>(services => new GatewayPlanCoordinator(bootstrap,
            services.GetRequiredService<ProxyConfigurationStore>(), plans, reconciler: null));
        builder.Services.AddSingleton(_ => new IpcIdentityInterceptor(bootstrap));
        builder.Services.AddSingleton(services => new ControlService(plans,
            services.GetRequiredService<ProxyAdminAuthenticationService>(),
            services.GetRequiredService<ProxyAdministrationDispatcher>()));
        builder.Services.AddSingleton<ProxyAdministrationDispatcher>();
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<IpcIdentityInterceptor>();
            options.MaxReceiveMessageSize = 8 * 1024 * 1024;
            options.MaxSendMessageSize = 8 * 1024 * 1024;
        });
        if (OperatingSystem.IsWindows()) builder.WebHost.UseNamedPipes(options => options.CurrentUserOnly = true);
        builder.WebHost.ConfigureKestrel(options =>
        {
            if (OperatingSystem.IsWindows())
                options.ListenNamedPipe(bootstrap.Listen.NamedPipeName, listener => listener.Protocols = HttpProtocols.Http2);
            else options.ListenUnixSocket(bootstrap.Listen.UnixSocketPath, listener => listener.Protocols = HttpProtocols.Http2);
        });
        var app = builder.Build();
        app.MapGrpcService<ControlService>();
        return app;
    }
}
