using Mk8.Drava.Application.BLL.ControlPlane.Status;

namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyRestoreValidationFindingPolicy
{
    public static ProxyRestoreValidationFinding FromBackupWarning(ProxyBackupWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return new ProxyRestoreValidationFinding(ProxyStatusText.Warning, warning.Code, warning.Message, warning.RelativePath);
    }

    public static ProxyRestoreValidationFinding RequiredDirectoryMissing(string relativePath)
    {
        return new ProxyRestoreValidationFinding(ProxyStatusText.Error, "required_directory_missing", "A required restore directory is missing.", relativePath);
    }

    public static ProxyRestoreValidationFinding ConfigurationError(string error, string? relativePath)
    {
        var finding = ClassifyConfigurationError(error);
        return new ProxyRestoreValidationFinding(ProxyStatusText.Error, finding.Code, finding.Message, relativePath);
    }

    public static ProxyRestoreValidationFindingShape ClassifyConfigurationError(string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = ClassifyConfigurationErrorCode(error);
        var message = code switch
        {
            "certificate_file_missing" => "Referenced certificate material is missing.",
            "certificate_reference_missing" => "A listener references an unknown configured certificate id.",
            "config_parse_failed" => "A configuration file could not be parsed.",
            _ => "Configuration validation failed."
        };
        return new ProxyRestoreValidationFindingShape(code, message);
    }

    private static string ClassifyConfigurationErrorCode(string error)
    {
        if (error.Contains("Certificate", StringComparison.OrdinalIgnoreCase) && error.Contains("file does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return "certificate_file_missing";
        }

        if (error.Contains("unknown certificate", StringComparison.OrdinalIgnoreCase))
        {
            return "certificate_reference_missing";
        }

        if (error.Contains("JSON", StringComparison.OrdinalIgnoreCase) || error.Contains("YAML", StringComparison.OrdinalIgnoreCase))
        {
            return "config_parse_failed";
        }

        return "config_validation_failed";
    }
}
