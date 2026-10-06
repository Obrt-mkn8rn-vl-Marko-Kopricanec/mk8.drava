using Mk8.Drava.Application.BLL.ControlPlane.AdminAudit;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using System.Net;

namespace Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
public static class ProxyAdminRequestAuthenticationInputMapper
{
    public static ProxyAdminRequestAuthenticationInput FromRawRequestFacts(string method, string? path, IPAddress? remoteClientAddress, IEnumerable<string?> authorizationHeaders, IEnumerable<string?> apiKeyHeaders)
    {
        return new ProxyAdminRequestAuthenticationInput(method, string.IsNullOrEmpty(path) ? "/" : path, ProxyClientAddressPolicy.NormalizeClientIp(remoteClientAddress), ProxyAdminPresentedCredentials.FromRawHeaders(authorizationHeaders, apiKeyHeaders));
    }
}
