namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record SiteConfigurationSource
{
    public const string NormalizeInputPath = "normalize-input";
    public const string LintInputPath = "lint-input";
    private SiteConfigurationSource(string path, SiteOptions site)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(site);
        Path = path;
        Site = site;
    }

    public string Path { get; }
    public SiteOptions Site { get; }

    public static SiteConfigurationSource FromFile(string path, SiteOptions site)
    {
        return new SiteConfigurationSource(path, site);
    }

    public static SiteConfigurationSource FromNormalizeInput(SiteOptions site)
    {
        return new SiteConfigurationSource(NormalizeInputPath, site);
    }

    public static SiteConfigurationSource FromLintInput(SiteOptions site)
    {
        return new SiteConfigurationSource(LintInputPath, site);
    }
}
