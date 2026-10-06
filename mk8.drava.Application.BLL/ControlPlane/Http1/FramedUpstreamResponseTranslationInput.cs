using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;
public sealed record FramedUpstreamResponseTranslationInput
{
    public FramedUpstreamResponseTranslationInput(int StatusCode, IReadOnlyList<ProxyHeaderField> Headers, bool ResponseEndedWithHead)
    {
        this.StatusCode = StatusCode;
        this.Headers = ProxyHeaderFieldList.Copy(Headers);
        this.ResponseEndedWithHead = ResponseEndedWithHead;
    }

    public int StatusCode { get; }
    public IReadOnlyList<ProxyHeaderField> Headers { get; }
    public bool ResponseEndedWithHead { get; }
}
