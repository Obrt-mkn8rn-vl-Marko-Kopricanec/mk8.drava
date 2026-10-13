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
    private readonly byte[] _credential;
    private readonly Lock _disposeGate = new();
    private Task? _disposeTask;
    public NativeDnsManagementSettings Settings { get; }

    private DevelopmentNativeDnsManagement(WebApplication host, string directory, NativeDnsManagementSettings settings, byte[] credential)
    {
        _host = host;
        _directory = directory;
        _credential = credential;
        Settings = settings;
    }

    public static async Task<DevelopmentNativeDnsManagement> StartAsync(Func<DevelopmentNativeDnsRequest, HttpContext, Task> handler,
        IReadOnlyList<NativeDnsOwnerScope>? scopes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var directory = Directory.CreateTempSubdirectory("drava_dns_").FullName;
        byte[] credential = [];
        WebApplication? host = null;
        var transferred = false;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            credential = RandomNumberGenerator.GetBytes(32);
            var settings = CreateSettings(directory, scopes, credential, cancellationToken);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(settings.SocketPath));
            host = builder.Build();
            ConfigureRequests(host, handler, credential);
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            var fixture = new DevelopmentNativeDnsManagement(host, directory, settings, credential);
            transferred = true;
            return fixture;
        }
        finally
        {
            if (!transferred)
            {
                try
                {
                    if (host is not null) await host.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(credential);
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }

    private static NativeDnsManagementSettings CreateSettings(string directory,
        IReadOnlyList<NativeDnsOwnerScope>? scopes, byte[] credential, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new NativeDnsManagementSettings
        {
            Origin = "site.test",
            TenantId = Guid.NewGuid(),
            ZoneId = Guid.NewGuid(),
            SocketPath = Path.Combine(directory, "management.sock"),
            ProfileId = Guid.NewGuid(),
            Credential = new SecretText(ManagementHttpProtocol.EncodeCredential(credential)[7..]),
            Scopes = scopes ?? [new NativeDnsOwnerScope("svc.site.test", 1)],
        };
    }

    private static void ConfigureRequests(WebApplication host, Func<DevelopmentNativeDnsRequest, HttpContext, Task> handler, byte[] credential)
    {
        host.Run(async context =>
        {
            if (!string.Equals(context.Request.Headers.Authorization.ToString(), ManagementHttpProtocol.EncodeCredential(credential), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Fixture received an invalid private capability.");
            }
            var request = await ReadRequestAsync(context).ConfigureAwait(false);
            await handler(request, context).ConfigureAwait(false);
        });
    }

    private static async Task<DevelopmentNativeDnsRequest> ReadRequestAsync(HttpContext context)
    {
        var action = string.Equals(context.Request.Method, "PATCH", StringComparison.Ordinal) ? "patch"
            : string.Equals(context.Request.Method, "GET", StringComparison.Ordinal) ? "status" : "read";
        Guid operation;
        ManagementApiRequest? body = null;
        if (string.Equals(action, "status", StringComparison.Ordinal))
        {
            operation = Guid.ParseExact(context.Request.Path.Value!.Split('/')[7], "D");
            if (context.Request.Headers.ContainsKey("Idempotency-Key") || context.Request.Headers.ContainsKey("X-Mk8-Request-Id"))
            {
                throw new InvalidDataException("Status request changed its header-only contract.");
            }
        }
        else
        {
            operation = Guid.ParseExact(context.Request.Headers[string.Equals(action, "patch", StringComparison.Ordinal) ? "Idempotency-Key" : "X-Mk8-Request-Id"].ToString(), "D");
            var buffer = new MemoryStream();
            await using var bufferLifetime = buffer.ConfigureAwait(false);
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            body = ManagementHttpProtocol.ReadRequest(buffer.ToArray());
        }
        return new DevelopmentNativeDnsRequest(action, operation, body, context.Request.Path + context.Request.QueryString);
    }

    public static async Task ReplyAsync(HttpContext context, ManagementReply reply, string? location = null)
    {
        context.Response.StatusCode = string.Equals(reply.State, "accepted", StringComparison.Ordinal) && string.Equals(context.Request.Method, "PATCH", StringComparison.Ordinal) ? 202 : 200;
        context.Response.Headers[ManagementHttpProtocol.VersionHeader] = ManagementHttpProtocol.Version;
        context.Response.ContentType = "application/json";
        if (location is not null) context.Response.Headers.Location = location;
        await context.Response.Body.WriteAsync(ManagementHttpProtocol.WriteReply(reply), context.RequestAborted).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _host.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(_credential);
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
