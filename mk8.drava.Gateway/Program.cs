using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Mk8.Drava.Configuration;
using Mk8.Drava.Presentation.Proxy;
using Mk8.Drava.Transport.Clients;

namespace Mk8.Drava.Gateway;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--bootstrap", StringComparison.Ordinal))
            throw new ArgumentException("Use --bootstrap with an absolute Gateway bootstrap file.", nameof(args));
        var bootstrap = await BootstrapFile.LoadAsync<GatewayBootstrap>(args[1], CancellationToken.None).ConfigureAwait(false);
        bootstrap.Validate();
        if (bootstrap.HttpsPort != 0) throw new NotSupportedException("TLS presentation requires the Application serving certificate plan.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Services.AddSingleton(bootstrap);
        builder.Services.AddSingleton(_ => new ApplicationChannel(bootstrap.Application));
        builder.Services.AddSingleton<GatewayProxy>();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestHeadersTotalSize = bootstrap.MaxHeaderBytes;
            options.Limits.MaxRequestHeaderCount = 128;
            options.Limits.MaxRequestLineSize = 8192;
            options.Limits.MaxRequestBodySize = bootstrap.MaxRequestBodyBytes;
            options.Limits.MaxRequestBufferSize = 64 * 1024;
            options.Limits.MaxResponseBufferSize = 64 * 1024;
            options.Listen(IPAddress.Parse(bootstrap.BindAddress), bootstrap.HttpPort, listener => listener.Protocols = HttpProtocols.Http1);
        });
        var app = builder.Build();
        await using var lifetime = app.ConfigureAwait(false);
        app.Run(context => app.Services.GetRequiredService<GatewayProxy>().InvokeAsync(context));
        await app.RunAsync().ConfigureAwait(false);
    }
}
