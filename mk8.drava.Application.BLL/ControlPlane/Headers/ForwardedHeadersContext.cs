using Mk8.Drava.Application.BLL.Http;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public sealed record ForwardedHeadersContext
{
    public ForwardedHeadersContext(string? ResolvedClientAddress, string? ResolvedClientEndpoint, IReadOnlyList<ProxyHeaderField> Headers)
    {
        this.ResolvedClientAddress = ResolvedClientAddress;
        this.ResolvedClientEndpoint = ResolvedClientEndpoint;
        this.Headers = ProxyHeaderFieldList.Copy(Headers);
    }

    public string? ResolvedClientAddress { get; }
    public string? ResolvedClientEndpoint { get; }
    public IReadOnlyList<ProxyHeaderField> Headers { get; }
}
