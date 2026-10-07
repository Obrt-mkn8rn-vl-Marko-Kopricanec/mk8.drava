using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Mk8.Drava.Registration;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentHttpUpstream : IAsyncDisposable
{
    private readonly WebApplication _application;
    public int Port { get; }
    public DravaRegistrationState RegistrationState => _application.Services.GetRequiredService<DravaRegistrationState>();

    private DevelopmentHttpUpstream(WebApplication application, int port) { _application = application; Port = port; }

    public static async Task<DevelopmentHttpUpstream> StartAsync(RequestDelegate handler, X509Certificate2? certificate = null, Action<string?>? onSni = null, DravaRegistrationOptions? registration = null, bool http2 = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0, listener =>
        {
            if (http2) listener.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
            if (certificate is not null) listener.UseHttps(https => https.ServerCertificateSelector = (_, name) => { onSni?.Invoke(name); return certificate; });
        }));
        if (registration is not null) builder.Services.AddDravaRegistration(registration);
        var application = builder.Build();
        try
        {
            application.Run(handler);
            await application.StartAsync().ConfigureAwait(false);
            var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Development upstream has no bound addresses.");
            var address = addresses.Addresses.First();
            return new DevelopmentHttpUpstream(application, new Uri(address).Port);
        }
        catch { await application.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }

    public Task StopAsync() => _application.StopAsync();
}
