namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public interface IForwardedHeadersAddressPolicy
{
    bool IsTrustedPeer(string peerAddress, IReadOnlyList<string> trustedProxyEntries);
    ForwardedForNormalizationResult NormalizeForwardedFor(IReadOnlyList<string> forwardedFor);
}
