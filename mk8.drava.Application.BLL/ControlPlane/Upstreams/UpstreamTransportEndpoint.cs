using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
public sealed record UpstreamTransportEndpoint(string Name, string Scheme, string Protocol, string Address, int Port, bool ValidateCertificate, string? SniHost)
{
    public string Endpoint => $"{Address}:{Port}";
    public string EffectiveSniHost => string.IsNullOrWhiteSpace(SniHost) ? Address : SniHost!;
    public string PoolKey => $"{Protocol}|{Scheme}|{Address}|{Port}|sni={EffectiveSniHost}|validate={ValidateCertificate}";
}
