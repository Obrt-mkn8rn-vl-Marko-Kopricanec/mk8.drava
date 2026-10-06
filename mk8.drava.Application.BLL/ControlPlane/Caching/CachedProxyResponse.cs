using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed class CachedProxyResponse
{
    private readonly byte[] _body;
    public CachedProxyResponse(int statusCode, string reasonPhrase, IReadOnlyList<ProxyHeaderField> headers, byte[] body, DateTimeOffset storedAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(body);
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Headers = CacheList.Copy(headers);
        _body = body.ToArray();
        StoredAtUtc = storedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public int StatusCode { get; }
    public string ReasonPhrase { get; }
    public IReadOnlyList<ProxyHeaderField> Headers { get; }
    public byte[] Body => _body.ToArray();
    public DateTimeOffset StoredAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
}
