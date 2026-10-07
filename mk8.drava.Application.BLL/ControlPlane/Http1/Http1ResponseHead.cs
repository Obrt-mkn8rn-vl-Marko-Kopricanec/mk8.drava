using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public sealed class Http1ResponseHead
{
    public Http1ResponseHead(string version, int statusCode, string reasonPhrase, Http1ResponseFraming framing, IEnumerable<ProxyHeaderField> headers, bool isHeadResponse = false)
    {
        ArgumentNullException.ThrowIfNull(headers);
        Version = version;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Framing = framing;
        IsHeadResponse = isHeadResponse;
        Headers = ProxyHeaderFieldList.Copy(headers);
    }

    public string Version { get; }
    public int StatusCode { get; }
    public string ReasonPhrase { get; }
    public Http1ResponseFraming Framing { get; }
    public bool IsHeadResponse { get; }
    public IReadOnlyList<ProxyHeaderField> Headers { get; }
}
