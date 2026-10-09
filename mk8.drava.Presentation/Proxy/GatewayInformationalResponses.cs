using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace Mk8.Drava.Presentation.Proxy;

public static class GatewayInformationalResponses
{
    public static void Configure(ListenOptions listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        listener.Use(next => connection => InvokeAsync(connection, next, listener.Protocols));
    }

    private static async Task InvokeAsync(ConnectionContext connection, ConnectionDelegate next, HttpProtocols protocols)
    {
        var original = connection.Transport;
        var alpn = connection.Features.Get<ITlsApplicationProtocolFeature>();
        var http2 = protocols == HttpProtocols.Http2 || alpn?.ApplicationProtocol.Span.SequenceEqual("h2"u8) == true;
        var output = new GatewayResponseOutput(connection, http2);
        connection.Transport = new GatewayResponseDuplexPipe(original.Input, output.Writer);
        connection.Features.Set<IGatewayInformationalResponseFeature>(output);
        try { await next(connection).ConfigureAwait(false); }
        finally
        {
            try { await output.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                connection.Transport = original;
                connection.Features.Set<IGatewayInformationalResponseFeature>(null);
            }
        }
    }
}
