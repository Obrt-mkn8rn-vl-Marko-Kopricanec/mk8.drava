using System.Net.Security;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;
public sealed record TlsAuthenticationResult
{
    private TlsAuthenticationResult(SslStream stream, SslApplicationProtocol negotiatedProtocol)
    {
        Stream = stream;
        NegotiatedProtocol = negotiatedProtocol;
    }

    public SslStream Stream { get; }
    public SslApplicationProtocol NegotiatedProtocol { get; }
    public bool NegotiatedHttp2 => NegotiatedProtocol == SslApplicationProtocol.Http2;

    public static TlsAuthenticationResult Succeeded(SslStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new TlsAuthenticationResult(stream, stream.NegotiatedApplicationProtocol);
    }
}
