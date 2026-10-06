using System.Globalization;
using System.Text;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.BLL.Proxy;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Http1;

namespace Mk8.Drava.Application.INF.Proxy.Exchange;

public sealed class MdravaProxyExchangeExecutor(ExchangeClientStream stream, ProxyForwarder forwarder, UpgradeForwarder upgrades) : IProxyExchangeExecutor
{
    public ValueTask<ForwardingResult> ForwardAsync(ProxyForwardingContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return forwarder.ForwardAsync(stream, Http1HeadReadResult.TranslatedRequestBody(ReadOnlyMemory<byte>.Empty), context.Head,
            context.Route, context.Upstream, context.Listener, context.Timeouts, context.ConnectionLimits, context.Limits,
            context.UpstreamTarget, context.ForwardedHeaders, preferClientKeepAlive: false, context.RequestId, cancellationToken, context.SuppressFailureResponse);
    }

    public ValueTask<ForwardingResult> UpgradeAsync(ProxyForwardingContext context, UpgradeRequestInfo upgrade, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return upgrades.ForwardAsync(stream, context.Head, upgrade, context.Route, context.Upstream, context.Listener,
            context.Timeouts, context.ConnectionLimits, context.UpstreamTarget, context.ForwardedHeaders, context.RequestId, cancellationToken);
    }

    public ValueTask GeneratedAsync(GeneratedRouteResponse response, string requestId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var headers = new List<ProxyHeaderField>(response.Headers);
        if (response.ContentType is not null) headers.Add(new ProxyHeaderField("Content-Type", response.ContentType));
        return WriteAsync(response.StatusCode, response.ReasonPhrase, headers, Encoding.UTF8.GetBytes(response.Body), requestId, cancellationToken);
    }

    public ValueTask CachedAsync(CachedProxyResponse response, string requestId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        return WriteAsync(response.StatusCode, response.ReasonPhrase, response.Headers, response.Body, requestId, cancellationToken);
    }

    private async ValueTask WriteAsync(int statusCode, string reason, IReadOnlyList<ProxyHeaderField> headers, ReadOnlyMemory<byte> body,
        string requestId, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        text.Append("HTTP/1.1 ").Append(statusCode.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reason).Append("\r\n");
        foreach (var field in headers)
            if (!string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(field.Name, "transfer-encoding", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(field.Name, "connection", StringComparison.OrdinalIgnoreCase))
                text.Append(field.Name).Append(": ").Append(field.Value).Append("\r\n");
        text.Append("X-Request-Id: ").Append(requestId).Append("\r\nContent-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n\r\n");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text.ToString()), cancellationToken).ConfigureAwait(false);
        if (body.Length != 0 && !string.Equals(stream.Method, "HEAD", StringComparison.OrdinalIgnoreCase) && statusCode is not 204 and not 304)
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }
}
