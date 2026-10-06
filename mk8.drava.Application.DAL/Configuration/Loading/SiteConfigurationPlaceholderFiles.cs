namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public static class SiteConfigurationPlaceholderFiles
{
    public const string ExampleSiteFileName = "example.site.yaml";
    private static readonly HashSet<string> PlaceholderFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ExampleSiteFileName,
        "example.site.yml",
        "example.site.json"
    };
    public static bool IsPlaceholderFileName(string fileName)
    {
        return PlaceholderFileNames.Contains(fileName);
    }
}
