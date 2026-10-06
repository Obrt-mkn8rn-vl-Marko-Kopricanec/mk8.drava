namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public sealed record ProxyReadinessStatus
{
    private const int MaxReasons = 12;
    private ProxyReadinessStatus(string state, IEnumerable<string> reasons, DateTimeOffset generatedAtUtc, int? configGeneration)
    {
        State = state;
        Reasons = ProxyStatusList.Copy(reasons);
        GeneratedAtUtc = generatedAtUtc;
        ConfigGeneration = configGeneration;
    }

    public string State { get; }
    public IReadOnlyList<string> Reasons { get; }
    public DateTimeOffset GeneratedAtUtc { get; }
    public int? ConfigGeneration { get; }
    public static ProxyReadinessStatus Unknown { get; } = Evaluated(ProxyStatusText.Unknown, [ProxyStatusText.NotAvailable], DateTimeOffset.UnixEpoch, configGeneration: null);

    public static ProxyReadinessStatus Evaluated(string state, IReadOnlyList<string> reasons, DateTimeOffset generatedAtUtc, int? configGeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(reasons);
        ProxyStatusFacts.RequireOptionalNonNegative(configGeneration, nameof(configGeneration));
        return new ProxyReadinessStatus(state, reasons.Where(static reason => !string.IsNullOrWhiteSpace(reason)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxReasons), generatedAtUtc, configGeneration);
    }
}
