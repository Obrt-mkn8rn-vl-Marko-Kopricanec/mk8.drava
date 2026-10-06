namespace Mk8.Drava.Application.BLL.Configuration;
public static class SecretRedactor
{
    public const string RedactedValue = "***REDACTED***";
    public static string? RedactConfiguredSecret(bool hasConfiguredSecret)
    {
        return hasConfiguredSecret ? RedactedValue : null;
    }
}
