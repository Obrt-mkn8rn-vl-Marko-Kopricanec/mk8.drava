using System.Net;
using System.Security.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Grpc.Core;
using Mk8.Drava.Configuration;
using Mk8.Drava.Presentation.Certificates;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Presentation.Registration;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Gateway;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--bootstrap", StringComparison.Ordinal) || !Path.IsPathFullyQualified(args[1]))
            throw new ArgumentException("Use --bootstrap with an absolute Gateway bootstrap file.", nameof(args));
        var bootstrap = await BootstrapFile.LoadAsync<GatewayBootstrap>(args[1], CancellationToken.None).ConfigureAwait(false);
        bootstrap.Validate();
        using var channel = new ApplicationChannel(bootstrap.Application);
        using var material = await LoadMaterialAsync(bootstrap, channel).ConfigureAwait(false);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Services.AddSingleton(bootstrap);
        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton<GatewayProxy>();
        if (material is not null)
        {
            builder.Services.AddGrpc(options => { options.MaxReceiveMessageSize = 64 * 1024; options.MaxSendMessageSize = 64 * 1024; });
            builder.Services.AddSingleton(services => new GatewayRegistrationService(services.GetRequiredService<ApplicationChannel>(), bootstrap));
        }
        ConfigureListeners(builder, bootstrap, material);
        var app = builder.Build();
        await using var lifetime = app.ConfigureAwait(false);
        if (material is not null) app.MapGrpcService<GatewayRegistrationService>();
        app.MapFallback(context =>
        {
            if (context.Connection.LocalPort != bootstrap.RegistrationPort)
                return app.Services.GetRequiredService<GatewayProxy>().InvokeAsync(context);
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        });
        await app.StartAsync().ConfigureAwait(false);
        if (material is not null) await AcknowledgeAsync(channel, material.Plan).ConfigureAwait(false);
        await app.WaitForShutdownAsync().ConfigureAwait(false);
    }

    private static async Task<GatewayServingMaterial?> LoadMaterialAsync(GatewayBootstrap bootstrap, ApplicationChannel channel)
    {
        if (bootstrap.EnrollmentRootFingerprint.Length == 0)
        {
            if (bootstrap.HttpsPort != 0) throw new InvalidDataException("TLS presentation requires enrolled site trust and its certificate plan.");
            return null;
        }
        var client = new ApplicationControl.ApplicationControlClient(channel.Invoker);
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            try
            {
                using var call = client.GatewayPlanAsync(new GatewayIdentity { Version = 1, GatewayId = bootstrap.GatewayId }, channel.Credentials,
                    deadline: DateTime.UtcNow.AddSeconds(3), cancellationToken: startup.Token);
                return new GatewayServingMaterial(await call.ResponseAsync.ConfigureAwait(false), bootstrap);
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Unavailable)
            {
                await Task.Delay(100, startup.Token).ConfigureAwait(false);
            }
        }
    }

    private static async Task AcknowledgeAsync(ApplicationChannel channel, PresentationPlan plan)
    {
        var client = new ApplicationControl.ApplicationControlClient(channel.Invoker);
        using var call = client.AcknowledgePlanAsync(new PlanAcknowledgment
        {
            Version = 1, GatewayId = plan.GatewayId, Generation = plan.Generation, ContentSha256 = plan.ContentSha256, Applied = true,
        }, channel.Credentials, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: CancellationToken.None);
        if ((await call.ResponseAsync.ConfigureAwait(false)).StatusCode != 200) throw new InvalidDataException("Application rejected the applied Gateway generation.");
    }

    private static void ConfigureListeners(WebApplicationBuilder builder, GatewayBootstrap bootstrap, GatewayServingMaterial? material)
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
            if (material is null) return;
            if (bootstrap.HttpsPort > 0)
                options.Listen(IPAddress.Parse(bootstrap.BindAddress), bootstrap.HttpsPort, listener =>
                {
                    listener.Protocols = HttpProtocols.Http1AndHttp2;
                    listener.UseHttps(material.ServingCertificate, https => https.SslProtocols = SslProtocols.None);
                });
            options.Listen(IPAddress.Parse(bootstrap.BindAddress), bootstrap.RegistrationPort, listener =>
            {
                listener.Protocols = HttpProtocols.Http2;
                listener.UseHttps(material.ServingCertificate, https =>
                {
                    https.SslProtocols = SslProtocols.None;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    https.ClientCertificateValidation = (certificate, _, _) => material.ValidateClientCertificate(certificate);
                });
            });
        });
    }
}
