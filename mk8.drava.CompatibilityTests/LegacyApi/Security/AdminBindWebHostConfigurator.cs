using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyApi.Security;
internal static class AdminBindWebHostConfigurator
{
    public const string MdravaAdminUrlsConfigurationKey = "Mdrava:Admin:Urls";
    public const string AspNetCoreUrlsConfigurationKey = "urls";
    public static AdminBindResolution Apply(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var dataOptions = new MdravaDataDirectoryOptions();
        builder.Configuration.GetSection(MdravaDataDirectoryOptions.SectionName).Bind(dataOptions);
        var startupSecurity = AdminStartupConfigurationReader.Read(dataOptions);
        var resolution = Resolve(builder.Configuration, startupSecurity);
        if (resolution.ApplyToWebHost)
        {
            builder.WebHost.UseUrls(resolution.Urls.ToArray());
        }

        return resolution;
    }

    public static AdminBindResolution Resolve(IConfiguration configuration, AdminStartupSecurityOptions startupSecurity)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return AdminBindPolicy.Resolve(AdminBindPolicyInputMapper.FromStartupConfiguration(startupSecurity, ReadConfiguredUrls(configuration, MdravaAdminUrlsConfigurationKey), MdravaAdminUrlsConfigurationKey, ReadConfiguredUrls(configuration, AspNetCoreUrlsConfigurationKey), AspNetCoreUrlsConfigurationKey), new ProxyAdminUrlPolicy());
    }

    private static string[] ReadConfiguredUrls(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        var children = section.GetChildren().Select(static child => child.Value).Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!.Trim()).ToArray();
        if (children.Length > 0)
        {
            return children;
        }

        var value = section.Value ?? configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return[];
        }

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
