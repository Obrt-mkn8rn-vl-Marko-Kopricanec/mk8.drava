using Microsoft.AspNetCore.Server.Kestrel.Core;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.Transport;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.BLL.Proxy;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--bootstrap", StringComparison.Ordinal))
            throw new ArgumentException("Use --bootstrap with an absolute Application bootstrap file.", nameof(args));
        var bootstrap = await BootstrapFile.LoadAsync<ApplicationBootstrap>(args[1], CancellationToken.None).ConfigureAwait(false);
        bootstrap.Validate();
        using var lifetime = PrivateApplicationState.Open(bootstrap);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration["Mdrava:DataDirectory"] = bootstrap.StateDirectory;
        builder.Services.AddSingleton(bootstrap);
        builder.Services.AddProxyApplication(builder.Configuration);
        builder.Services.AddSingleton<IMdravaDataDirectoryProvider>(new ApplicationDataDirectoryProvider(bootstrap.StateDirectory));
        builder.Services.AddSingleton(_ => new IpcIdentityInterceptor(bootstrap));
        builder.Services.AddSingleton(_ => new ExchangeAdmission(bootstrap));
        builder.Services.AddSingleton(services => new ProxyExchangeService(services.GetRequiredService<ProxyRequestPipeline>(),
            services.GetRequiredService<ProxyForwarder>(), services.GetRequiredService<UpgradeForwarder>(), bootstrap, services.GetRequiredService<ExchangeAdmission>()));
        builder.Services.AddGrpc(options => { options.Interceptors.Add<IpcIdentityInterceptor>(); options.MaxReceiveMessageSize = 8 * 1024 * 1024; options.MaxSendMessageSize = 8 * 1024 * 1024; });
        builder.Services.AddGrpc().AddServiceOptions<ProxyExchangeService>(options => { options.MaxReceiveMessageSize = 64 * 1024; options.MaxSendMessageSize = 64 * 1024; });
        ConfigurePrivateListener(builder, bootstrap);
        var app = builder.Build();
        await using var appLifetime = app.ConfigureAwait(false);
        await RuntimeInitializer.InitializeAsync(app.Services, CancellationToken.None).ConfigureAwait(false);
        app.MapGrpcService<ProxyExchangeService>();
        await app.RunAsync().ConfigureAwait(false);
    }

    private static void ConfigurePrivateListener(WebApplicationBuilder builder, ApplicationBootstrap bootstrap)
    {
        if (bootstrap.Listen.HttpsAddress.Length > 0) throw new NotSupportedException("Remote Application listener requires the enrolled server role configuration.");
        if (bootstrap.Listen.NamedPipeName.Length > 0)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Private named pipes require Windows.");
            builder.WebHost.UseNamedPipes(options => options.CurrentUserOnly = true);
        }
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.Http2.InitialStreamWindowSize = 64 * 1024;
            options.Limits.Http2.InitialConnectionWindowSize = 256 * 1024;
            options.Limits.Http2.MaxStreamsPerConnection = 128;
            options.Limits.MaxRequestBodySize = null;
            if (bootstrap.Listen.UnixSocketPath.Length > 0)
                options.ListenUnixSocket(bootstrap.Listen.UnixSocketPath, listener => listener.Protocols = HttpProtocols.Http2);
            else if (OperatingSystem.IsWindows())
                options.ListenNamedPipe(bootstrap.Listen.NamedPipeName, listener => listener.Protocols = HttpProtocols.Http2);
        });
    }
}
