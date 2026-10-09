using System.Net;
using System.Security.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Gateway.Hosting;
using Mk8.Drava.Configuration;
using Mk8.Drava.Presentation.Certificates;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Presentation.Registration;
using Mk8.Drava.Presentation.Administration;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Discovery;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Gateway;

internal static class Program
{
    private static readonly Action<ILogger, Exception?> DiscoveryUnavailable = LoggerMessage.Define(LogLevel.Warning,
        new EventId(1, nameof(DiscoveryUnavailable)), "Local service discovery is unavailable; registration remains available through configured addresses.");

    public static async Task Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--bootstrap", StringComparison.Ordinal) || !Path.IsPathFullyQualified(args[1]))
            throw new ArgumentException("Use --bootstrap with an absolute Gateway bootstrap file.", nameof(args));
        using var startup = new GatewayStartupProgress();
        startup.Enter(GatewayStartupPhase.BootstrapRead);
        var bootstrap = await BootstrapFile.LoadAsync<GatewayBootstrap>(args[1], CancellationToken.None).ConfigureAwait(false);
        bootstrap.Validate();
        startup.Enter(GatewayStartupPhase.PrivateChannelOpen);
        using var channel = new ApplicationChannel(bootstrap.Application);
        using var cache = new GatewayPlanCache(bootstrap.StateDirectory);
        using var material = new GatewayMaterialState(bootstrap.Plan.MaximumRetainedGenerations);
        var enrolled = bootstrap.EnrollmentRootFingerprint.Length != 0;
        if (!enrolled && bootstrap.HttpsPort != 0) throw new InvalidDataException("TLS presentation requires enrolled site trust.");
        if (enrolled)
        {
            startup.Enter(GatewayStartupPhase.CachedMaterialRestore);
            var cached = await cache.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (cached is not null) InstallCachedMaterial(material, cached, bootstrap);
        }
        startup.Enter(GatewayStartupPhase.ServiceConfiguration);
        var builder = CreateBuilder(bootstrap, channel, cache, material, enrolled);
        startup.Enter(GatewayStartupPhase.ServiceProviderBuild);
        var app = builder.Build();
        await using var lifetime = app.ConfigureAwait(false);
        MapPresentation(app, bootstrap, enrolled);
        startup.Enter(GatewayStartupPhase.ListenerStart);
        await app.StartAsync().ConfigureAwait(false);
        startup.Complete();
        var advertisement = enrolled && bootstrap.DiscoveryEnabled ? TryAdvertise(bootstrap, app.Logger) : null;
        if (advertisement is null) await app.WaitForShutdownAsync().ConfigureAwait(false);
        else
        {
            await using var discoveryLifetime = advertisement.ConfigureAwait(false);
            await app.WaitForShutdownAsync().ConfigureAwait(false);
        }
    }

    private static WebApplicationBuilder CreateBuilder(GatewayBootstrap bootstrap, ApplicationChannel channel, GatewayPlanCache cache,
        GatewayMaterialState material, bool enrolled)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Services.AddSingleton(bootstrap);
        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton<GatewayProxy>();
        builder.Services.AddSingleton<GatewayAdministrationClient>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ProxyStatusController).Assembly);
        if (enrolled)
        {
            builder.Services.AddSingleton(material);
            builder.Services.AddSingleton(cache);
            builder.Services.AddHostedService<GatewayPlanService>();
            builder.Services.AddGrpc(options => { options.MaxReceiveMessageSize = 64 * 1024; options.MaxSendMessageSize = 64 * 1024; });
            builder.Services.AddSingleton(services => new GatewayRegistrationService(services.GetRequiredService<ApplicationChannel>(), bootstrap));
        }
        ConfigureListeners(builder, bootstrap, material);
        return builder;
    }

    private static void MapPresentation(WebApplication app, GatewayBootstrap bootstrap, bool enrolled)
    {
        app.Use((context, next) =>
        {
            var management = bootstrap.ManagementPort > 0 && context.Connection.LocalPort == bootstrap.ManagementPort;
            var administrationPath = context.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);
            if (management != administrationPath)
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            }
            return next(context);
        });
        app.MapControllers();
        if (enrolled) app.MapGrpcService<GatewayRegistrationService>();
        app.MapGet("/_drava/live", () => Results.Text("running"));
        app.MapFallback(context =>
        {
            if (context.Connection.LocalPort != bootstrap.RegistrationPort && context.Connection.LocalPort != bootstrap.ManagementPort)
                return app.Services.GetRequiredService<GatewayProxy>().InvokeAsync(context);
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        });
    }

    private static GatewaySiteAdvertisement? TryAdvertise(GatewayBootstrap bootstrap, ILogger logger)
    {
        try { return new GatewaySiteAdvertisement(bootstrap); }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or InvalidOperationException or NotSupportedException)
        {
            DiscoveryUnavailable(logger, exception);
            return null;
        }
    }

    private static void InstallCachedMaterial(GatewayMaterialState state, PresentationPlan plan, GatewayBootstrap bootstrap)
    {
        GatewayServingMaterial? candidate = GatewayServingMaterial.Restore(plan, bootstrap);
        try { state.Install(candidate); candidate = null; }
        finally { candidate?.Dispose(); }
    }

    private static void ConfigureListeners(WebApplicationBuilder builder, GatewayBootstrap bootstrap, GatewayMaterialState material)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestHeadersTotalSize = bootstrap.MaxHeaderBytes;
            options.Limits.MaxRequestHeaderCount = 128;
            options.Limits.MaxRequestLineSize = 8192;
            options.Limits.MaxRequestBodySize = bootstrap.MaxRequestBodyBytes;
            options.Limits.MaxRequestBufferSize = 64 * 1024;
            options.Limits.MaxResponseBufferSize = 64 * 1024;
            if (bootstrap.HttpPort > 0)
                options.Listen(IPAddress.Parse(bootstrap.BindAddress), bootstrap.HttpPort, listener => listener.Protocols = HttpProtocols.Http1);
            if (bootstrap.EnrollmentRootFingerprint.Length == 0) return;
            if (bootstrap.HttpsPort > 0)
                options.Listen(IPAddress.Parse(bootstrap.BindAddress), bootstrap.HttpsPort, listener =>
                {
                    listener.Protocols = HttpProtocols.Http1AndHttp2;
                    ConfigureTls(listener, material, bootstrap.Plan, clientCertificate: false);
                });
            options.Listen(IPAddress.Parse(bootstrap.BindAddress), bootstrap.RegistrationPort, listener =>
            {
                listener.Protocols = HttpProtocols.Http2;
                ConfigureTls(listener, material, bootstrap.Plan, clientCertificate: true);
            });
            if (bootstrap.ManagementPort > 0)
                options.Listen(IPAddress.Parse(bootstrap.BindAddress), bootstrap.ManagementPort, listener =>
                {
                    listener.Protocols = HttpProtocols.Http1AndHttp2;
                    ConfigureTls(listener, material, bootstrap.Plan, clientCertificate: true);
                });
        });
    }

    private static void ConfigureTls(ListenOptions listener, GatewayMaterialState material, GatewayPlanSettings settings, bool clientCertificate)
    {
        listener.Use(next => async connection =>
        {
            using var lease = material.Acquire();
            connection.Features.Set(lease);
            await next(connection).ConfigureAwait(false);
        });
        listener.UseHttps(https =>
        {
            https.SslProtocols = SslProtocols.None;
            https.HandshakeTimeout = TimeSpan.FromSeconds(settings.TlsHandshakeSeconds);
            https.ServerCertificateSelector = (connection, _) => GatewayMaterialState.SelectCertificate(connection, enrollment: clientCertificate);
            https.OnAuthenticate = (connection, options) => GatewayMaterialState.ConfigureCertificateContext(connection, options, enrollment: clientCertificate);
            if (clientCertificate)
            {
                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                https.ClientCertificateValidation = (certificate, _, _) => material.ValidateClientCertificate(certificate);
            }
        });
    }
}
