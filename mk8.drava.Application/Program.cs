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
        if (args.Length == 2 && string.Equals(args[0], "--initialize-site", StringComparison.Ordinal) && Path.IsPathFullyQualified(args[1]))
        {
            var fingerprint = await Installation.SiteInitializer.InitializeAsync(args[1], CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine(fingerprint);
            return;
        }
        if (args.Length == 2 && string.Equals(args[0], "--node-agent-bootstrap", StringComparison.Ordinal) && Path.IsPathFullyQualified(args[1]))
        {
            await NodeAgentHost.RunAsync(args[1]).ConfigureAwait(false);
            return;
        }
        if (args.Length == 5 && string.Equals(args[0], "--initialize-ca", StringComparison.Ordinal) && string.Equals(args[3], "--site", StringComparison.Ordinal))
        {
            if (!string.Equals(args[1], "--path", StringComparison.Ordinal)) throw new ArgumentException("Issuer initialization requires --path.", nameof(args));
            var fingerprint = await LocalSiteCertificateAuthority.InitializeAsync(args[2], args[4], TimeProvider.System, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine(fingerprint);
            return;
        }
        if (args.Length != 2 || !string.Equals(args[0], "--bootstrap", StringComparison.Ordinal) || !Path.IsPathFullyQualified(args[1]))
            throw new ArgumentException("Use --bootstrap with an absolute Application bootstrap file.", nameof(args));
        using var startup = new ApplicationStartupProgress();
        startup.Enter(ApplicationStartupPhase.BootstrapRead);
        var bootstrap = await BootstrapFile.LoadAsync<ApplicationBootstrap>(args[1], CancellationToken.None).ConfigureAwait(false);
        bootstrap.Validate();
        startup.Enter(ApplicationStartupPhase.PrivateStateOpen);
        var privateState = await PrivateApplicationState.OpenAsync(bootstrap, CancellationToken.None).ConfigureAwait(false);
        await using var privateStateLifetime = privateState.ConfigureAwait(false);
        startup.Enter(ApplicationStartupPhase.ServiceConfiguration);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration["Mdrava:DataDirectory"] = bootstrap.StateDirectory;
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
            services.GetRequiredService<ProxyForwarder>(), services.GetRequiredService<UpgradeForwarder>(), bootstrap, services.GetRequiredService<ExchangeAdmission>()));
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
        builder.Services.AddOwnerAcmeLifecycle(bootstrap, registration.Plans,
            registration.AcmeHistory(bootstrap.Controller!.Domain, bootstrap.Controller.Acme.DirectoryUrl), registration.DnsJournal);
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
