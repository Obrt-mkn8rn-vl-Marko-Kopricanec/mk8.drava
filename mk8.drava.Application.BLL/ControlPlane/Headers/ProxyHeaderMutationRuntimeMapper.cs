using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public static class ProxyHeaderMutationRuntimeMapper
{
    public static ProxyHeaderMutationPolicyInput ToPolicyInput(RuntimeHeaderPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new ProxyHeaderMutationPolicyInput(policy.SetRequestHeaders, policy.RemoveRequestHeaders, policy.SetResponseHeaders, policy.RemoveResponseHeaders);
    }
}
