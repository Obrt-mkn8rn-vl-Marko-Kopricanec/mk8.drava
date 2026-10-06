namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeHttp3CompatibilityFacts
{
    private const RuntimeListenerProtocols SupportedProtocols = RuntimeListenerProtocols.Http1AndHttp2AndHttp3;
    public static void Validate(RuntimeListenerProtocols protocols, bool protocolsValid, RuntimeHttp3Enablement effectiveEnablement, bool enablementValid, bool enablementExplicitlyConfigured, bool explicitHttp3Requested)
    {
        ValidateProtocols(protocols, nameof(protocols));
        ValidateEnablement(effectiveEnablement, nameof(effectiveEnablement));
        if (explicitHttp3Requested != protocols.HasHttp3())
        {
            throw new ArgumentException("Explicit HTTP/3 request must match listener protocols.", nameof(explicitHttp3Requested));
        }

        if (!protocolsValid && protocols != RuntimeListenerProtocols.Http1)
        {
            throw new ArgumentException("Invalid protocol parsing must fall back to HTTP/1.", nameof(protocols));
        }

        if (!enablementValid)
        {
            if (effectiveEnablement != RuntimeHttp3Enablement.Default)
            {
                throw new ArgumentException("Invalid HTTP/3 enablement must fall back to default.", nameof(effectiveEnablement));
            }

            if (!enablementExplicitlyConfigured)
            {
                throw new ArgumentException("Invalid HTTP/3 enablement must come from an explicit value.", nameof(enablementExplicitlyConfigured));
            }
        }

        if (enablementValid && !enablementExplicitlyConfigured && effectiveEnablement != RuntimeHttp3Enablement.Default)
        {
            throw new ArgumentException("Implicit HTTP/3 enablement must resolve to default.", nameof(effectiveEnablement));
        }
    }

    public static void ValidateProtocols(RuntimeListenerProtocols protocols, string parameterName)
    {
        if (protocols == RuntimeListenerProtocols.None || (protocols & ~SupportedProtocols) != 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void ValidateEnablement(RuntimeHttp3Enablement enablement, string parameterName)
    {
        if (enablement is not RuntimeHttp3Enablement.Default and not RuntimeHttp3Enablement.Disabled)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
