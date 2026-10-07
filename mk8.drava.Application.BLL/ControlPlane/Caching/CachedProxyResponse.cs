using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public sealed class CachedProxyResponse
{
    private readonly byte[] _body;
    public CachedProxyResponse(int statusCode, string reasonPhrase, IReadOnlyList<ProxyHeaderField> headers, byte[] body, DateTimeOffset storedAtUtc, DateTimeOffset expiresAtUtc)
        : this(statusCode, reasonPhrase, headers, body, storedAtUtc, expiresAtUtc, GetBodyLength(body)) { }

    internal CachedProxyResponse(int statusCode, string reasonPhrase, IReadOnlyList<ProxyHeaderField> headers, byte[] body,
        DateTimeOffset storedAtUtc, DateTimeOffset expiresAtUtc, long? contentLength)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(body);
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Headers = CacheList.Copy(headers);
        _body = body.ToArray();
        ContentLength = contentLength;
        StoredAtUtc = storedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    private static long GetBodyLength(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.LongLength;
    }

    public long? ContentLength { get; }
    public int StatusCode { get; }
    public string ReasonPhrase { get; }
    public IReadOnlyList<ProxyHeaderField> Headers { get; }
    public byte[] Body => _body.ToArray();
    public DateTimeOffset StoredAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
}
