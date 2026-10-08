namespace Mk8.Drava.Application.BLL.Configuration;
internal static class ProxyHeaderPolicyOptionsValidationRules
{
    public static void Validate(List<string> failures, string routePrefix, ProxyHeaderPolicyOptions policy)
    {
        ValidateHeaderSetRules(failures, $"{routePrefix}:HeaderPolicy:SetRequestHeaders", policy.SetRequestHeaders);
        ValidateHeaderSetRules(failures, $"{routePrefix}:HeaderPolicy:SetResponseHeaders", policy.SetResponseHeaders);
        ValidateHeaderRemoveRules(failures, $"{routePrefix}:HeaderPolicy:RemoveRequestHeaders", policy.RemoveRequestHeaders);
        ValidateHeaderRemoveRules(failures, $"{routePrefix}:HeaderPolicy:RemoveResponseHeaders", policy.RemoveResponseHeaders);
    }

    public static bool IsValidHttpFieldName(string headerName)
    {
        return ProxyHeaderPolicyFacts.IsValidHttpFieldName(headerName);
    }

    private static void ValidateHeaderSetRules(List<string> failures, string prefix, IList<ProxyHeaderSetOptions> rules)
    {
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var rulePrefix = $"{prefix}:{index}";
            ValidateHeaderName(failures, $"{rulePrefix}:Name", rule.Name);
            if (rule.Value.Any(static character => character is '\r' or '\n'))
            {
                failures.Add($"{rulePrefix}:Value must not contain CR or LF.");
            }
        }
    }

    private static void ValidateHeaderRemoveRules(List<string> failures, string prefix, IList<string> headerNames)
    {
        for (var index = 0; index < headerNames.Count; index++)
        {
            ValidateHeaderName(failures, $"{prefix}:{index}", headerNames[index]);
        }
    }

    private static void ValidateHeaderName(List<string> failures, string prefix, string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName))
        {
            failures.Add($"{prefix} is required.");
            return;
        }

        if (ProxyHeaderPolicyFacts.IsRestrictedHeaderName(headerName))
        {
            failures.Add($"{prefix} '{headerName}' is restricted and cannot be modified by header policy.");
            return;
        }

        if (!IsValidHttpFieldName(headerName))
        {
            failures.Add($"{prefix} '{headerName}' is not a valid HTTP field name.");
        }
    }
}
