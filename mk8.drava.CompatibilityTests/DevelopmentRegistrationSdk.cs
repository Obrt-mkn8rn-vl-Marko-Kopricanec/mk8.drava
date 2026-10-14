using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Mk8.Drava.Registration;
using Supprocom.Secrets;

namespace Mk8.Drava.CompatibilityTests;

internal static class DevelopmentRegistrationSdk
{
    internal static async Task RunAsync()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        { Args = [], EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrelCore().UseSockets().ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddRouting();
        try { await builder.Services.AddDravaRegistrationAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or SupprocomSecretsException or ArgumentException)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "SDK-CONFIG-REFUSED" }));
            Environment.ExitCode = 2;
            return;
        }
        var app = builder.Build();
        await using var lifetime = app.ConfigureAwait(false);
        var configured = app.Services.GetRequiredService<DravaRegistrationOptions>();
        app.MapGet(configured.ReadinessPath, static () => "ready");
        app.MapGet("/", static () => "protected-sdk-body");
        app.MapGet("/state", (DravaRegistrationState state) => Results.Json(new { phase = state.Status?.Phase.ToString() }));
        app.MapPost("/stop", (HttpContext context, IHostApplicationLifetime host) =>
        {
            context.Response.OnCompleted(() => { host.StopApplication(); return Task.CompletedTask; });
            return Results.Ok();
        });
        await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("SDK development host has no bound address.");
        var bound = addresses.Addresses.ToArray();
        if (bound.Length != 1) throw new InvalidOperationException("SDK development host requires exactly one bound address.");
        Console.WriteLine("SDK-READY " + JsonSerializer.Serialize(new { port = new Uri(bound[0]).Port }));
        await app.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
