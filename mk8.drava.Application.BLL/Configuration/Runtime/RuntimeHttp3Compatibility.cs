namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHttp3Compatibility
{
    public RuntimeHttp3Compatibility(RuntimeListenerProtocols Protocols, bool ProtocolsValid, RuntimeHttp3Enablement EffectiveEnablement, bool EnablementValid, bool EnablementExplicitlyConfigured, bool ExplicitHttp3Requested)
    {
        RuntimeHttp3CompatibilityFacts.Validate(Protocols, ProtocolsValid, EffectiveEnablement, EnablementValid, EnablementExplicitlyConfigured, ExplicitHttp3Requested);
        this.Protocols = Protocols;
        this.ProtocolsValid = ProtocolsValid;
        this.EffectiveEnablement = EffectiveEnablement;
        this.EnablementValid = EnablementValid;
        this.EnablementExplicitlyConfigured = EnablementExplicitlyConfigured;
        this.ExplicitHttp3Requested = ExplicitHttp3Requested;
    }

    public RuntimeListenerProtocols Protocols { get; }
    public bool ProtocolsValid { get; }
    public RuntimeHttp3Enablement EffectiveEnablement { get; }
    public bool EnablementValid { get; }
    public bool EnablementExplicitlyConfigured { get; }
    public bool ExplicitHttp3Requested { get; }

    public static readonly IReadOnlyList<string> SupportedProtocolConfigValues = ["http1", "http2", "http1AndHttp2", "http3", "http1AndHttp3", "http2AndHttp3", "http1AndHttp2AndHttp3"];
    public static readonly IReadOnlyList<string> SupportedEnablementConfigValues = ["default", "disabled"];
    public static RuntimeHttp3Compatibility From(ListenerOptions listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        var protocolParsing = ParseProtocols(listener.Protocols);
        var protocolsValid = protocolParsing is RuntimeListenerProtocolParseResult.AcceptedResult;
        var protocols = protocolParsing is RuntimeListenerProtocolParseResult.AcceptedResult acceptedProtocols ? acceptedProtocols.Protocols : RuntimeListenerProtocols.Http1;
        var enablementParsing = ParseEnablement(listener.Http3Enablement);
        var enablementValid = enablementParsing is RuntimeHttp3EnablementParseResult.AcceptedResult;
        var parsedEnablement = enablementParsing is RuntimeHttp3EnablementParseResult.AcceptedResult acceptedEnablement ? acceptedEnablement.Enablement : RuntimeHttp3Enablement.Default;
        var enablementExplicitlyConfigured = enablementParsing switch
        {
            RuntimeHttp3EnablementParseResult.AcceptedResult accepted => accepted.ExplicitlyConfigured,
            RuntimeHttp3EnablementParseResult.RejectedResult rejected => rejected.ExplicitlyConfigured,
            _ => false
        };
        var effectiveEnablement = enablementValid && enablementExplicitlyConfigured ? parsedEnablement : RuntimeHttp3Enablement.Default;
        return new RuntimeHttp3Compatibility(protocols, protocolsValid, effectiveEnablement, enablementValid, enablementExplicitlyConfigured, protocols.HasHttp3());
    }

    public static RuntimeListenerProtocolParseResult ParseProtocols(string? protocols)
    {
        if (string.IsNullOrWhiteSpace(protocols))
        {
            return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http1);
        }

        var token = protocols.Trim();
        if (token.Any(static character => !char.IsAscii(character))) return RuntimeListenerProtocolParseResult.Rejected;

        switch (token.ToUpperInvariant())
        {
            case "HTTP1":
                return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http1);
            case "HTTP2":
                return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http2);
            case "HTTP1ANDHTTP2":
                return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http1AndHttp2);
            case "HTTP3":
                return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http3);
            case "HTTP1ANDHTTP3":
                return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http1AndHttp3);
            case "HTTP2ANDHTTP3":
                return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http2AndHttp3);
            case "HTTP1ANDHTTP2ANDHTTP3":
                return RuntimeListenerProtocolParseResult.Accepted(RuntimeListenerProtocols.Http1AndHttp2AndHttp3);
            default:
                return RuntimeListenerProtocolParseResult.Rejected;
        }
    }

    public static RuntimeListenerProtocols ParseProtocolsOrDefault(string? protocols)
    {
        return ParseProtocols(protocols)is RuntimeListenerProtocolParseResult.AcceptedResult accepted ? accepted.Protocols : RuntimeListenerProtocols.Http1;
    }

    public static RuntimeHttp3EnablementParseResult ParseEnablement(string? enablement)
    {
        if (string.IsNullOrWhiteSpace(enablement))
        {
            return RuntimeHttp3EnablementParseResult.Accepted(RuntimeHttp3Enablement.Default, explicitlyConfigured: false);
        }

        var token = enablement.Trim();
        if (token.Any(static character => !char.IsAscii(character))) return RuntimeHttp3EnablementParseResult.Rejected(explicitlyConfigured: true);

        switch (token.ToUpperInvariant())
        {
            case "DEFAULT":
                return RuntimeHttp3EnablementParseResult.Accepted(RuntimeHttp3Enablement.Default, explicitlyConfigured: true);
            case "DISABLED":
                return RuntimeHttp3EnablementParseResult.Accepted(RuntimeHttp3Enablement.Disabled, explicitlyConfigured: true);
            default:
                return RuntimeHttp3EnablementParseResult.Rejected(explicitlyConfigured: true);
        }
    }

    public static RuntimeHttp3Enablement ResolveEffectiveEnablement(RuntimeHttp3Enablement configuredEnablement)
    {
        return configuredEnablement == RuntimeHttp3Enablement.Disabled ? RuntimeHttp3Enablement.Disabled : RuntimeHttp3Enablement.Default;
    }

    public static string MergeEnablementConfigText(string existing, string next)
    {
        var existingParsing = ParseEnablement(existing);
        if (existingParsing is RuntimeHttp3EnablementParseResult.RejectedResult && !string.IsNullOrWhiteSpace(existing))
        {
            return existing.Trim();
        }

        var nextParsing = ParseEnablement(next);
        if (nextParsing is RuntimeHttp3EnablementParseResult.RejectedResult && !string.IsNullOrWhiteSpace(next))
        {
            return next.Trim();
        }

        var existingAccepted = (RuntimeHttp3EnablementParseResult.AcceptedResult)existingParsing;
        var nextAccepted = (RuntimeHttp3EnablementParseResult.AcceptedResult)nextParsing;
        var existingEnablement = existingAccepted.Enablement;
        var nextEnablement = nextAccepted.Enablement;
        var existingExplicit = existingAccepted.ExplicitlyConfigured;
        var nextExplicit = nextAccepted.ExplicitlyConfigured;
        if (!existingExplicit && !nextExplicit)
        {
            return "";
        }

        return existingEnablement == RuntimeHttp3Enablement.Disabled || nextEnablement == RuntimeHttp3Enablement.Disabled ? RuntimeHttp3Enablement.Disabled.ToConfigText() : RuntimeHttp3Enablement.Default.ToConfigText();
    }
}
