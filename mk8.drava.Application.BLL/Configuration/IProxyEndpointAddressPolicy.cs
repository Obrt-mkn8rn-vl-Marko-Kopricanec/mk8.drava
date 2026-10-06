namespace Mk8.Drava.Application.BLL.Configuration;
public interface IProxyEndpointAddressPolicy
{
    bool IsListenerAddress(string value);
    bool IsAmbiguousUpstreamAddress(string value);
    bool IsValidSniHost(string value);
}
