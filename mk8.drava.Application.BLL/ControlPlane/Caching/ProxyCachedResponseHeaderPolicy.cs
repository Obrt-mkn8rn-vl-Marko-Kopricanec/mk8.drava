using System.Globalization;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.Caching;
public static class ProxyCachedResponseHeaderPolicy
{
    public static IReadOnlyList<ProxyHeaderField> BuildFramedResponseHeaders(CachedProxyResponse response, string requestId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var ageSeconds = ProxyCacheAgePolicy.CalculateAgeSeconds(response.StoredAtUtc, nowUtc);
        var fields = response.Headers.Where(static header => !HopByHopHeaderPolicy.IsHopByHopHeader(header.Name)
            && !string.Equals(header.Name, "content-length", StringComparison.OrdinalIgnoreCase))
            .Append(new ProxyHeaderField("age", ageSeconds.ToString(CultureInfo.InvariantCulture)))
            .Append(new ProxyHeaderField("x-request-id", requestId));
        if (response.ContentLength is { } length) fields = fields.Append(new ProxyHeaderField("content-length", length.ToString(CultureInfo.InvariantCulture)));
        return fields.ToArray();
    }
}
