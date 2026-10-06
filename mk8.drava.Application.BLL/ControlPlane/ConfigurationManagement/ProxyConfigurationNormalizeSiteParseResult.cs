using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
public abstract record ProxyConfigurationNormalizeSiteParseResult
{
    private ProxyConfigurationNormalizeSiteParseResult()
    {
    }

    public static ProxyConfigurationNormalizeSiteParseResult Parsed(SiteOptions site, string canonicalJson)
    {
        return new ParsedResult(site, canonicalJson);
    }

    public static ProxyConfigurationNormalizeSiteParseResult Failed(string error)
    {
        return new FailedResult(error);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ParsedResult : ProxyConfigurationNormalizeSiteParseResult
    {
        public ParsedResult(SiteOptions site, string canonicalJson)
        {
            ArgumentNullException.ThrowIfNull(site);
            ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
            Site = site;
            CanonicalJson = canonicalJson;
        }

        public SiteOptions Site { get; }
        public string CanonicalJson { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record FailedResult : ProxyConfigurationNormalizeSiteParseResult
    {
        public FailedResult(string error)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(error);
            Error = error;
        }

        public string Error { get; }
    }
}
