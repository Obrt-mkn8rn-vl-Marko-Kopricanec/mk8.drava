using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Mk8.Drava.Transport.Clients;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Administration;

public sealed class GatewayAdministrationClient(ApplicationChannel channel) : IDisposable
{
    private readonly SemaphoreSlim _admission = new(16, 16);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<IActionResult> ExecuteAsync(HttpContext context, ControlOperation operation, object? query = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!await _admission.WaitAsync(0, context.RequestAborted).ConfigureAwait(false)) return new StatusCodeResult(429);
        try
        {
            var credential = ReadCredential(context.Request);
            var payload = query is null ? await ReadPayloadAsync(context.Request, context.RequestAborted).ConfigureAwait(false)
                : context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                    ? throw new InvalidDataException("Query does not accept a request body.") : JsonSerializer.SerializeToUtf8Bytes(query, Options);
            if (payload.Length == 0 && HttpMethods.IsPost(context.Request.Method) && operation is ControlOperation.ConfigurationLint or ControlOperation.ConfigurationNormalize or ControlOperation.RouteDryRun)
                return new BadRequestResult();
            var request = new ControlRequest
            {
                Version = 1, Operation = operation, JsonPayload = ByteString.CopyFrom(payload), AdministratorCredential = credential,
                PeerAddress = context.Connection.RemoteIpAddress?.ToString() ?? "", RequestId = Guid.NewGuid().ToString("N"),
            };
            var client = new ApplicationControl.ApplicationControlClient(channel.Invoker);
            using var call = client.ExecuteAsync(request, channel.Credentials, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: context.RequestAborted);
            var reply = await call.ResponseAsync.ConfigureAwait(false);
            if (reply.StatusCode is < 200 or > 599 || reply.JsonPayload.Length > 4 * 1024 * 1024) return new StatusCodeResult(502);
            if (reply.StatusCode == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
            if (reply.JsonPayload.IsEmpty) return new StatusCodeResult(checked((int)reply.StatusCode));
            if (!string.Equals(reply.ContentType, "application/json", StringComparison.Ordinal) &&
                !string.Equals(reply.ContentType, "text/plain; version=0.0.4; charset=utf-8", StringComparison.Ordinal)) return new StatusCodeResult(502);
            context.Response.Headers.CacheControl = "no-store";
            return new ContentResult { StatusCode = checked((int)reply.StatusCode), ContentType = reply.ContentType, Content = reply.JsonPayload.ToStringUtf8() };
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413) { return new StatusCodeResult(413); }
        catch (InvalidDataException) { return new BadRequestResult(); }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted)
        {
            context.Response.Headers.RetryAfter = 1.ToString(CultureInfo.InvariantCulture);
            return new StatusCodeResult(503);
        }
        finally { _admission.Release(); }
    }

    private static string ReadCredential(HttpRequest request)
    {
        var authorization = request.Headers.Authorization;
        var key = request.Headers["X-MDRAVA-Admin-Key"];
        if (authorization.Count > 1 || key.Count > 1 || authorization.Count > 0 && key.Count > 0)
            throw new InvalidDataException("Administrator credential is ambiguous.");
        var value = authorization.Count == 1 ? authorization[0] ?? "" : key.Count == 1 ? "Bearer " + key[0] : "";
        if (value.Length > 512) throw new InvalidDataException("Administrator credential exceeds its bound.");
        return value;
    }

    private static async Task<byte[]> ReadPayloadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        const int maximum = 256 * 1024;
        if (request.ContentLength > maximum) throw new BadHttpRequestException("Control payload exceeds its bound.", 413);
        var bytes = new byte[maximum + 1];
        var used = 0;
        int count;
        while (used < bytes.Length && (count = await request.Body.ReadAsync(bytes.AsMemory(used), cancellationToken).ConfigureAwait(false)) > 0) used += count;
        if (used > maximum) throw new BadHttpRequestException("Control payload exceeds its bound.", 413);
        return bytes[..used];
    }

    public void Dispose() => _admission.Dispose();

}
