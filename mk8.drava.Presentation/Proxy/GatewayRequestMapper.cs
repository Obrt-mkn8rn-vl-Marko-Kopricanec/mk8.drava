using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Proxy;

public static class GatewayRequestMapper
{
    public static RequestHead ToHead(HttpContext context, GatewayBootstrap bootstrap, ulong generation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bootstrap);
        var request = context.Request;
        var head = new RequestHead
        {
            Version = FrameLimits.Version, ExchangeId = Guid.NewGuid().ToString("N"), GatewayId = bootstrap.GatewayId,
            GatewayGeneration = generation, Method = request.Method, RawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? "",
            Authority = request.Host.Value ?? "", Scheme = request.Scheme, ClientProtocol = request.Protocol,
            PeerAddress = context.Connection.RemoteIpAddress?.ToString() ?? "", PeerPort = (uint)context.Connection.RemotePort,
            HasBody = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? false,
            WantsUpgrade = context.Features.Get<IHttpUpgradeFeature>()?.IsUpgradableRequest ?? false,
            ListenerId = request.IsHttps ? "https" : "http",
            StreamWindowFrames = (uint)bootstrap.StreamWindowFrames,
        };
        if (request.ContentLength.HasValue) head.ContentLength = request.ContentLength.Value;
        foreach (var field in request.Headers)
            foreach (var value in field.Value)
                head.Headers.Add(new Header { Name = field.Key, Value = value ?? "" });
        FrameLimits.ValidateRequest(head);
        return head;
    }
}
