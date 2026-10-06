namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeAdminSecurityFacts
{
    public const int MinimumAuditCapacity = 1;
    public const int MaximumAuditCapacity = 10_000;
    public static void Validate(string tokenEnvironmentVariable, string tokenSource, int recentAuditCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenEnvironmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenSource);
        if (recentAuditCapacity is < MinimumAuditCapacity or > MaximumAuditCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(recentAuditCapacity));
        }
    }
}
