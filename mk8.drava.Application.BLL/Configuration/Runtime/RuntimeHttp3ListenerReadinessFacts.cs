namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeHttp3ListenerReadinessFacts
{
    public static void Validate(string enablementLevel, string disabledReason, int altSvcMaxAgeSeconds)
    {
        if (string.IsNullOrWhiteSpace(enablementLevel))
        {
            throw new ArgumentException("HTTP/3 enablement level is required.", nameof(enablementLevel));
        }

        if (string.IsNullOrWhiteSpace(disabledReason))
        {
            throw new ArgumentException("HTTP/3 disabled reason is required.", nameof(disabledReason));
        }

        RuntimeHttp3AltSvcFacts.Validate(altSvcMaxAgeSeconds);
    }
}
