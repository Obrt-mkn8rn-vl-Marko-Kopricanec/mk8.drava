namespace Mk8.Drava.Application.BLL.Http;
public static class ProxyRequestMethodPolicy
{
    public const string ConnectUnsupportedReason = "connect_unsupported";
    public const string MethodUnsupportedReason = "method_unsupported";
    private static readonly HashSet<string> SupportedApplicationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD",
        "POST",
        "PUT",
        "PATCH",
        "DELETE"
    };
    private static readonly HashSet<string> SafeReadMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD"
    };
    public static bool IsConnectTunnelMethod(string method)
    {
        return string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeReadMethod(string method)
    {
        return SafeReadMethods.Contains(method);
    }

    public static bool IsValidMethodToken(string method)
    {
        return !string.IsNullOrWhiteSpace(method) && method.All(static character => character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~' || character is >= 'A' and <= 'Z' || character is >= 'a' and <= 'z' || character is >= '0' and <= '9');
    }

    public static ProxyRequestApplicationMethodDecision ClassifyApplicationMethod(string method)
    {
        if (SupportedApplicationMethods.Contains(method))
        {
            return ProxyRequestApplicationMethodDecision.Supported;
        }

        var rejectionReason = IsConnectTunnelMethod(method) ? ConnectUnsupportedReason : MethodUnsupportedReason;
        return ProxyRequestApplicationMethodDecision.Rejected(rejectionReason);
    }
}
