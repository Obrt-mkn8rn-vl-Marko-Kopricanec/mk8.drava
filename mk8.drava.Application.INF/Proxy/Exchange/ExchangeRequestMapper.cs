using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.Proxy;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Proxy.Exchange;

public static class ExchangeRequestMapper
{
    public static ProxyRequest ToRequest(RequestHead request)
    {
        ArgumentNullException.ThrowIfNull(request);
        FrameLimits.ValidateRequest(request);
        var headers = new List<ProxyHeaderField>();
        foreach (var field in request.Headers)
        {
            if (string.Equals(field.Name, "host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.Name, "transfer-encoding", StringComparison.OrdinalIgnoreCase)) continue;
            headers.Add(new ProxyHeaderField(field.Name, field.Value));
        }
        headers.Add(new ProxyHeaderField("Host", request.Authority));
        var framing = request.HasBody ? Http1RequestFraming.Chunked : Http1RequestFraming.None;
        if (request.HasBody) headers.Add(new ProxyHeaderField("Transfer-Encoding", "chunked"));
        var query = request.RawTarget.IndexOf('?', StringComparison.Ordinal);
        var path = query < 0 ? request.RawTarget : request.RawTarget[..query];
        var head = new Http1RequestHead(request.Method, request.RawTarget, path, "HTTP/1.1", request.Authority, framing, headers);
        return new ProxyRequest(head, request.ListenerId, new ForwardedHeadersPeer(request.PeerAddress, $"{request.PeerAddress}:{request.PeerPort}"),
            request.ClientProtocol switch { "HTTP/1.1" => "http1", "HTTP/2" => "http2", "HTTP/3" => "http3", _ => throw new InvalidDataException("Unsupported client protocol.") },
            request.HasContentLength ? request.ContentLength : null);
    }
}
