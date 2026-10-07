using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public sealed class ProxyConfigurationNormalizer : IProxyConfigurationNormalizeOperations
{
    private readonly IProxyConfigurationNormalizeSiteParser _siteParser;
    private readonly IProxyEndpointAddressPolicy _endpointAddressPolicy;
    private readonly IProxyUrlSyntaxPolicy _urlSyntaxPolicy;
    public ProxyConfigurationNormalizer(IProxyConfigurationNormalizeSiteParser siteParser, IProxyEndpointAddressPolicy endpointAddressPolicy, IProxyUrlSyntaxPolicy urlSyntaxPolicy)
    {
        _siteParser = siteParser;
        _endpointAddressPolicy = endpointAddressPolicy;
        _urlSyntaxPolicy = urlSyntaxPolicy;
    }

    public ProxyConfigurationNormalizeResult Normalize(ProxyConfigurationNormalizeRequest? request)
    {
        if (request is null)
        {
            return ProxyConfigurationNormalizeResult.Failed("unknown", [ProxyConfigurationFileError.Global("A normalize request body is required.")]);
        }

        var formatDecision = ParseFormat(request.Format);
        if (formatDecision is not ProxyConfigurationNormalizeFormatDecision.Accepted acceptedFormat)
        {
            var error = ProxyConfigurationFileError.Global("Format must be 'json' or 'yaml'.");
            return ProxyConfigurationNormalizeResult.Failed(RequestFormatName(request.Format), [error]);
        }

        var format = acceptedFormat.Format;
        var formatName = FormatName(format);
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return ProxyConfigurationNormalizeResult.Failed(formatName, [ProxyConfigurationFileError.Global("Submitted config text is required.")]);
        }

        var parsed = _siteParser.Parse(request.Text, format);
        if (parsed is ProxyConfigurationNormalizeSiteParseResult.FailedResult failed)
        {
            return ProxyConfigurationNormalizeResult.Failed(formatName, [ProxyConfigurationFileError.Global(failed.Error)]);
        }

        var parsedSite = (ProxyConfigurationNormalizeSiteParseResult.ParsedResult)parsed;
        var options = SiteOptionsAggregator.ToProxyOptions([SiteConfigurationSource.FromNormalizeInput(parsedSite.Site)]);
        var validationFailures = ProxyOptionsValidationRules.Validate(options, _endpointAddressPolicy, _urlSyntaxPolicy);
        if (validationFailures.Count > 0)
        {
            return ProxyConfigurationNormalizeResult.Failed(formatName, validationFailures.Select(static failure => ProxyConfigurationFileError.Global(failure)).ToArray());
        }

        return ProxyConfigurationNormalizeResult.Normalized(formatName, parsedSite.CanonicalJson);
    }

    private static ProxyConfigurationNormalizeFormatDecision ParseFormat(string? format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return ProxyConfigurationNormalizeFormatDecision.Accept(ProxyConfigurationNormalizeFormat.Json);
        }

        if (string.Equals(format, "yaml", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "yml", StringComparison.OrdinalIgnoreCase))
        {
            return ProxyConfigurationNormalizeFormatDecision.Accept(ProxyConfigurationNormalizeFormat.Yaml);
        }

        return ProxyConfigurationNormalizeFormatDecision.Rejected;
    }

    private static string FormatName(ProxyConfigurationNormalizeFormat format)
    {
        return format == ProxyConfigurationNormalizeFormat.Yaml ? "yaml" : "json";
    }

    private static string RequestFormatName(string? format)
    {
        return string.IsNullOrWhiteSpace(format) ? "unknown" : format;
    }

    private abstract record ProxyConfigurationNormalizeFormatDecision
    {
        private ProxyConfigurationNormalizeFormatDecision()
        {
        }

        public static ProxyConfigurationNormalizeFormatDecision Rejected { get; } = new RejectedDecision();

        public static Accepted Accept(ProxyConfigurationNormalizeFormat format)
        {
            return new Accepted(format);
        }

        public sealed record Accepted(ProxyConfigurationNormalizeFormat Format) : ProxyConfigurationNormalizeFormatDecision;
        private sealed record RejectedDecision : ProxyConfigurationNormalizeFormatDecision;
    }
}
