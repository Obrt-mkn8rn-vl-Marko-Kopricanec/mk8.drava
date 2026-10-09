using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.INF.Dns.Management;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentNativeDnsManagement : IAsyncDisposable
{
    private readonly WebApplication _host;
    private readonly string _directory;
    public NativeDnsManagementSettings Settings { get; }

    private DevelopmentNativeDnsManagement(WebApplication host, string directory, NativeDnsManagementSettings settings)
    { _host = host; _directory = directory; Settings = settings; }

    public static async Task<DevelopmentNativeDnsManagement> StartAsync(Func<DevelopmentNativeDnsRequest, HttpContext, Task> handler,
        IReadOnlyList<NativeDnsOwnerScope>? scopes = null)
    {
        var directory = Directory.CreateTempSubdirectory("drava_dns_").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var credential = RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(directory, "capability");
        await File.WriteAllTextAsync(path, ManagementHttpProtocol.EncodeCredential(credential)[7..]).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var settings = new NativeDnsManagementSettings { Origin = "site.test", TenantId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), SocketPath = Path.Combine(directory, "management.sock"),
            CredentialPath = path, Scopes = scopes ?? [new NativeDnsOwnerScope("svc.site.test", 1)] };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(settings.SocketPath));
        var host = builder.Build();
        var success = false;
        try
        {
            host.Run(async context =>
            {
                if (!string.Equals(context.Request.Headers.Authorization.ToString(), ManagementHttpProtocol.EncodeCredential(credential), StringComparison.Ordinal))
                    throw new InvalidDataException("Fixture received an invalid private capability.");
                var action = string.Equals(context.Request.Method, "PATCH", StringComparison.Ordinal) ? "patch" : string.Equals(context.Request.Method, "GET", StringComparison.Ordinal) ? "status" : "read";
                Guid operation;
                ManagementApiRequest? body = null;
                if (string.Equals(action, "status", StringComparison.Ordinal))
                {
                    operation = Guid.ParseExact(context.Request.Path.Value!.Split('/')[7], "D");
                    if (context.Request.Headers.ContainsKey("Idempotency-Key") || context.Request.Headers.ContainsKey("X-Mk8-Request-Id"))
                        throw new InvalidDataException("Status request changed its header-only contract.");
                }
                else
                {
                    operation = Guid.ParseExact(context.Request.Headers[string.Equals(action, "patch", StringComparison.Ordinal) ? "Idempotency-Key" : "X-Mk8-Request-Id"].ToString(), "D");
                    using var buffer = new MemoryStream();
                    await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
                    body = ManagementHttpProtocol.ReadRequest(buffer.ToArray());
                }
                await handler(new DevelopmentNativeDnsRequest(action, operation, body, context.Request.Path + context.Request.QueryString), context).ConfigureAwait(false);
            });
            await host.StartAsync().ConfigureAwait(false);
            success = true; return new DevelopmentNativeDnsManagement(host, directory, settings);
        }
        finally
        {
            if (!success) { await host.DisposeAsync().ConfigureAwait(false); Directory.Delete(directory, recursive: true); }
        }
    }

    public static async Task ReplyAsync(HttpContext context, ManagementReply reply, string? location = null)
    {
        context.Response.StatusCode = string.Equals(reply.State, "accepted", StringComparison.Ordinal) && string.Equals(context.Request.Method, "PATCH", StringComparison.Ordinal) ? 202 : 200;
        context.Response.Headers[ManagementHttpProtocol.VersionHeader] = ManagementHttpProtocol.Version;
        context.Response.ContentType = "application/json";
        if (location is not null) context.Response.Headers.Location = location;
        await context.Response.Body.WriteAsync(ManagementHttpProtocol.WriteReply(reply), context.RequestAborted).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _host.StopAsync().ConfigureAwait(false); }
        finally { await _host.DisposeAsync().ConfigureAwait(false); Directory.Delete(_directory, recursive: true); }
    }
}
