using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Paths;

namespace Mk8.Drava.Application.DAL.Configuration.Loading;
public static class AdminStartupConfigurationReader
{
    public static AdminStartupSecurityOptions Read(MdravaDataDirectoryOptions dataOptions)
    {
        var dataDirectory = new MdravaDataDirectoryProvider(dataOptions);
        var proxyConfigPath = dataDirectory.GetProxyOperationalConfigPath();
        if (!File.Exists(proxyConfigPath))
        {
            return new AdminStartupSecurityOptions([], false, false);
        }

        try
        {
            using var stream = File.OpenRead(proxyConfigPath);
            var options = JsonSerializer.Deserialize<ProxyOperationalOptions>(stream, SiteConfigurationParser.ReadJsonOptions);
            if (options is null)
            {
                return new AdminStartupSecurityOptions([], false, false);
            }

            var token = ProxyAdminSecurityTokenPolicy.Resolve(options.Admin, Environment.GetEnvironmentVariable).Token;
            return new AdminStartupSecurityOptions(ProxyAdminSecurityTokenPolicy.NormalizeUrls(options.Admin.Urls), options.Admin.RequireAuthentication, !string.IsNullOrEmpty(token));
        }
        catch (JsonException)
        {
            return new AdminStartupSecurityOptions([], false, false);
        }
        catch (IOException)
        {
            return new AdminStartupSecurityOptions([], false, false);
        }
        catch (UnauthorizedAccessException)
        {
            return new AdminStartupSecurityOptions([], false, false);
        }
    }
}
