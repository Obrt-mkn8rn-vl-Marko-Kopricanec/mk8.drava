using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentHttpUpstream : IAsyncDisposable
{
    private readonly WebApplication _application;
    public int Port { get; }

    private DevelopmentHttpUpstream(WebApplication application, int port) { _application = application; Port = port; }

    public static async Task<DevelopmentHttpUpstream> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
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
}
