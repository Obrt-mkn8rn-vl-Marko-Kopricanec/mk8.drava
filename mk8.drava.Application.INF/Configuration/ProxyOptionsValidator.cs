using Mk8.Drava.Application.BLL.Configuration;
using Microsoft.Extensions.Options;

namespace Mk8.Drava.Application.INF.Configuration;
public sealed class ProxyOptionsValidator : IValidateOptions<ProxyOptions>
{
    private readonly IProxyEndpointAddressPolicy _endpointAddressPolicy;
    private readonly IProxyUrlSyntaxPolicy _urlSyntaxPolicy;
    public ProxyOptionsValidator(IProxyEndpointAddressPolicy endpointAddressPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy)
    {
        _endpointAddressPolicy = endpointAddressPolicy;
        _urlSyntaxPolicy = urlSyntaxPolicy;
    }

    public ValidateOptionsResult Validate(string? name, ProxyOptions options)
    {
        var failures = ProxyOptionsValidationRules.Validate(options, _endpointAddressPolicy, _urlSyntaxPolicy);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
