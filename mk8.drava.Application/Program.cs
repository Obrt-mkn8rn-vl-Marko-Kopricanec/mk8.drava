using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mk8.Drava.Application.Hosting;
using Mk8.Drava.Application.Transport;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.BLL.Proxy;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Application.INF.Administration;
using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Application.INF.Runtime;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (Recovery.ConfiguredSiteRestoreCommand.Recognizes(args))
        {
            await Recovery.ConfiguredSiteRestoreCommand.RunAsync(args, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        if (Recovery.SiteRecoveryCommand.Recognizes(args))
        {
            await Recovery.SiteRecoveryCommand.RunAsync(args, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        if (Recovery.RegistryRecoveryCommand.Recognizes(args))
        {
            await Recovery.RegistryRecoveryCommand.RunAsync(args, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        if (args is ["--initialize-site"])
        {
            var fingerprint = await Installation.SiteInitializer.InitializeAsync(PortableConfiguration.StartupPath([]), CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine(fingerprint);
            return;
        }
        if (args is ["--node-agent"])
        {
            await NodeAgentHost.RunAsync(PortableConfiguration.StartupPath([])).ConfigureAwait(false);
            return;
        }
        await RunApplicationAsync(PortableConfiguration.StartupPath(args)).ConfigureAwait(false);
    }

    private static async Task RunApplicationAsync(string bootstrapPath)
    {
        using var startup = new ApplicationStartupProgress();
        startup.Enter(ApplicationStartupPhase.BootstrapRead);
        var bootstrap = await BootstrapFile.LoadAsync<ApplicationBootstrap>(bootstrapPath, CancellationToken.None).ConfigureAwait(false);
        bootstrap.Validate();
        startup.Enter(ApplicationStartupPhase.PrivateStateOpen);
        var privateState = await PrivateApplicationState.OpenAsync(bootstrap, CancellationToken.None).ConfigureAwait(false);
        await using var privateStateLifetime = privateState.ConfigureAwait(false);
        startup.Enter(ApplicationStartupPhase.ServiceConfiguration);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrelCore().UseSockets();
        builder.Services.AddRouting();
        builder.Logging.AddSimpleConsole();
        if (bootstrap.ForwardingDiagnostics) builder.Logging.AddFilter("Mk8.Drava.Application.INF.Proxy.Forwarding", LogLevel.Debug);
        if (bootstrap.TransportDiagnostics) builder.Logging.AddFilter("Mk8.Drava.Application.Transport", LogLevel.Debug);
        // Structured proxy access events provide the safe path. Framework request
        // start/finish events also print QueryString and must not duplicate them.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Mdrava:DataDirectory"] = bootstrap.StateDirectory });
        builder.Services.AddSingleton(bootstrap);
        builder.Services.AddProxyApplication(builder.Configuration);
        var availability = new DestinationAvailabilityStore(TimeProvider.System);
        builder.Services.RemoveAll<DestinationAvailabilityStore>();
        builder.Services.AddSingleton(availability);
        startup.Enter(ApplicationStartupPhase.RegistrationStateOpen);
        var registration = bootstrap.Controller is null ? null : await RegistrationRuntime.OpenAsync(bootstrap, availability, TimeProvider.System, CancellationToken.None).ConfigureAwait(false);
        try
        {
            ConfigureServices(builder, bootstrap, registration);
            ConfigurePrivateListener(builder, bootstrap);
            await RunHostAsync(builder, registration, startup).ConfigureAwait(false);
        }
        finally
        {
            if (registration is not null) await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void ConfigureServices(WebApplicationBuilder builder, ApplicationBootstrap bootstrap, RegistrationRuntime? registration)
    {
        builder.Services.AddSingleton<IProxyListenerReloadApplier>(services => new GatewayPlanCoordinator(bootstrap,
            services.GetRequiredService<ProxyConfigurationStore>(), registration?.Plans, registration is null ? null : services.GetRequiredService<NoConfReconciler>()));
        builder.Services.AddSingleton<IMdravaDataDirectoryProvider>(new ApplicationDataDirectoryProvider(bootstrap.StateDirectory));
        builder.Services.AddSingleton(_ => new IpcIdentityInterceptor(bootstrap));
        builder.Services.AddSingleton(_ => new ExchangeAdmission(bootstrap));
        builder.Services.AddSingleton(services => new ProxyExchangeService(services.GetRequiredService<ProxyRequestPipeline>(),
            services.GetRequiredService<ProxyForwarder>(), services.GetRequiredService<UpgradeForwarder>(), bootstrap, services.GetRequiredService<ExchangeAdmission>(),
            services.GetRequiredService<ILogger<ProxyExchangeService>>()));
        builder.Services.AddGrpc(options => { options.Interceptors.Add<IpcIdentityInterceptor>(); options.MaxReceiveMessageSize = 8 * 1024 * 1024; options.MaxSendMessageSize = 8 * 1024 * 1024; });
        builder.Services.AddGrpc().AddServiceOptions<ProxyExchangeService>(options => { options.MaxReceiveMessageSize = 64 * 1024; options.MaxSendMessageSize = 64 * 1024; });
        builder.Services.RemoveAll<IProxyAdminSecurityOptionsReader>();
        builder.Services.AddSingleton<IProxyAdminSecurityOptionsReader>(_ => new EnrolledAdministratorSecurityReader(bootstrap.AdministratorTokenPath));
        builder.Services.AddSingleton<ProxyAdministrationDispatcher>();
        builder.Services.AddSingleton(services => new ControlService(registration?.Plans, services.GetRequiredService<ProxyAdminAuthenticationService>(), services.GetRequiredService<ProxyAdministrationDispatcher>()));
        if (registration is null) return;
        builder.Services.AddSingleton(registration.Registry);
        builder.Services.AddSingleton(registration.Handler);
        builder.Services.AddSingleton(registration.Plans);
        if (bootstrap.Controller is { } controller && controller.Acme.Enabled)
        {
            builder.Services.AddOwnerAcmeLifecycle(bootstrap, registration.Plans,
                registration.AcmeHistory(controller.Domain, controller.Acme.RequireDirectoryUrl()), registration.DnsJournal);
        }
        builder.Services.AddHostedService<ServingPlanMaintenance>();
        builder.Services.AddSingleton(services => new RegistrationService(services.GetRequiredService<Mk8.Drava.Application.INF.Registry.SignedRegistrationHandler>()));
        builder.Services.AddNoConfRuntime(bootstrap, registration);
        builder.Services.AddSingleton(services => new NoConfAdministration(services.GetRequiredService<NoConfReconciler>(), registration.Registry,
            services.GetRequiredService<DestinationAvailabilityStore>(), bootstrap.SiteId));
        builder.Services.AddGrpc().AddServiceOptions<RegistrationService>(options => { options.MaxReceiveMessageSize = 64 * 1024; options.MaxSendMessageSize = 64 * 1024; });
    }

    private static async Task RunHostAsync(WebApplicationBuilder builder, RegistrationRuntime? registration, ApplicationStartupProgress startup)
    {
        startup.Enter(ApplicationStartupPhase.ServiceProviderBuild);
        var app = builder.Build();
        await using var appLifetime = app.ConfigureAwait(false);
        startup.Enter(ApplicationStartupPhase.RuntimeConfigurationLoad);
        await RuntimeInitializer.InitializeAsync(app.Services, CancellationToken.None).ConfigureAwait(false);
        // Construct authentication and request/control services before publishing IPC.
        startup.Enter(ApplicationStartupPhase.ServingServicesInitialize);
        _ = app.Services.GetRequiredService<IpcIdentityInterceptor>();
        _ = app.Services.GetRequiredService<ProxyExchangeService>();
        _ = app.Services.GetRequiredService<ControlService>();
        if (registration is not null)
        {
            startup.Enter(ApplicationStartupPhase.AutomaticRoutesInitialize);
            await app.Services.GetRequiredService<NoConfReconciler>().InitializeAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        app.MapGrpcService<ProxyExchangeService>();
        app.MapGrpcService<ControlService>();
        if (registration is not null)
        {
            app.MapGrpcService<RegistrationService>();
        }
        var started = app.Lifetime.ApplicationStarted.Register(static state => ((ApplicationStartupProgress)state!).Complete(), startup);
        await using var startedLifetime = started.ConfigureAwait(false);
        startup.Enter(ApplicationStartupPhase.ListenerStart);
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
            options.Limits.Http2.InitialStreamWindowSize = bootstrap.PrivateHttp2.InitialStreamWindowBytes;
            options.Limits.Http2.InitialConnectionWindowSize = bootstrap.PrivateHttp2.InitialConnectionWindowBytes;
            options.Limits.Http2.MaxStreamsPerConnection = bootstrap.PrivateHttp2.MaximumStreamsPerConnection;
            options.Limits.MaxRequestBodySize = null;
            if (bootstrap.Listen.UnixSocketPath.Length > 0)
                options.ListenUnixSocket(bootstrap.Listen.UnixSocketPath, listener => listener.Protocols = HttpProtocols.Http2);
            else if (OperatingSystem.IsWindows())
                options.ListenNamedPipe(bootstrap.Listen.NamedPipeName, listener => listener.Protocols = HttpProtocols.Http2);
        });
    }
}
