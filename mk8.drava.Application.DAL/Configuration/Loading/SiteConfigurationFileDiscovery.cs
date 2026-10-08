using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public static class SiteConfigurationFileDiscovery
{
    public static IReadOnlyList<(string Path, SiteConfigurationFormat Format)> DiscoverLoadableSiteFiles(string sitesDirectory, ICollection<ProxyConfigurationFileDiscovery> discoveries)
    {
        ArgumentNullException.ThrowIfNull(discoveries);
        if (!Directory.Exists(sitesDirectory))
        {
            discoveries.Add(new ProxyConfigurationFileDiscovery(sitesDirectory, "directory", "missing", "Sites configuration directory does not exist."));
            return[];
        }

        var files = Directory.EnumerateFiles(sitesDirectory, "*.*", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        List<(string Path, SiteConfigurationFormat Format)> loadable = [];
        foreach (var file in files)
        {
            if (TryGetFormat(file, out var format))
            {
                if (SiteConfigurationPlaceholderFiles.IsPlaceholderFileName(System.IO.Path.GetFileName(file)))
                {
                    discoveries.Add(new ProxyConfigurationFileDiscovery(file, FormatName(format), "skipped", "Generated placeholder/example file is ignored."));
                    continue;
                }

                loadable.Add((file, format));
                continue;
            }

            discoveries.Add(new ProxyConfigurationFileDiscovery(file, "unknown", "skipped", "File extension is not .json, .yaml, or .yml."));
        }

        return loadable;
    }

    public static bool TryParseFormat(string format, out SiteConfigurationFormat parsed)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            parsed = SiteConfigurationFormat.Json;
            return true;
        }

        if (string.Equals(format, "yaml", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "yml", StringComparison.OrdinalIgnoreCase))
        {
            parsed = SiteConfigurationFormat.Yaml;
            return true;
        }

        parsed = SiteConfigurationFormat.Json;
        return false;
    }

    public static string FormatName(SiteConfigurationFormat format)
    {
        return format == SiteConfigurationFormat.Yaml ? "yaml" : "json";
    }

    private static bool TryGetFormat(string path, out SiteConfigurationFormat format)
    {
        var extension = System.IO.Path.GetExtension(path);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            format = SiteConfigurationFormat.Json;
            return true;
        }

        if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
        {
            format = SiteConfigurationFormat.Yaml;
            return true;
        }

        format = SiteConfigurationFormat.Json;
        return false;
    }
}
