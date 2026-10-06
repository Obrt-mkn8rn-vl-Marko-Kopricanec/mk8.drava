using Mk8.Drava.Application.BLL.Http;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public static class ProxyHeaderMutationPolicy
{
    public static IReadOnlyList<ProxyHeaderField> ApplyRequestHeaders(IReadOnlyList<ProxyHeaderField> headers, ProxyHeaderMutationPolicyInput policy, ForwardedHeadersContext forwardedHeaders)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(forwardedHeaders);
        var result = headers.Where(header => !ForwardedHeadersPolicy.IsForwardedHeader(header.Name)).Where(header => !ContainsHeaderName(policy.RemoveRequestHeaders, header.Name)).Where(header => !ContainsHeaderName(policy.SetRequestHeaders.Select(static set => set.Name), header.Name)).ToList();
        result.AddRange(policy.SetRequestHeaders);
        foreach (var forwardedHeader in forwardedHeaders.Headers)
        {
            result.RemoveAll(header => string.Equals(header.Name, forwardedHeader.Name, StringComparison.OrdinalIgnoreCase));
            result.Add(forwardedHeader);
        }

        return result;
    }

    public static IReadOnlyList<ProxyHeaderField> ApplyResponseHeaders(IReadOnlyList<ProxyHeaderField> headers, ProxyHeaderMutationPolicyInput policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var result = headers.Where(header => !ContainsHeaderName(policy.RemoveResponseHeaders, header.Name)).Where(header => !ContainsHeaderName(policy.SetResponseHeaders.Select(static set => set.Name), header.Name)).ToList();
        result.AddRange(policy.SetResponseHeaders);
        return result;
    }

    private static bool ContainsHeaderName(IEnumerable<string> headerNames, string headerName)
    {
        return headerNames.Any(name => string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase));
    }
}
