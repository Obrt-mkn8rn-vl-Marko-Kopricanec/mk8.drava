using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
public static class UpstreamTransportEndpointMapper
{
    public static UpstreamTransportEndpoint FromUpstream(RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        return new UpstreamTransportEndpoint(upstream.Name, upstream.Scheme, upstream.Protocol, upstream.Address, upstream.Port, upstream.Tls.ValidateCertificate, upstream.Tls.SniHost)
        {
            MembershipPartition = upstream.Membership?.Partition ?? "",
            TrustedRoot = upstream.Tls.TrustedRoot,
        };
    }
}
