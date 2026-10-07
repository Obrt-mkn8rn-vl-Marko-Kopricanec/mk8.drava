namespace Mk8.Drava.Configuration;

public sealed record ServingPlanSettings
{
    public int LeafLifetimeDays { get; init; } = 30;
    public int RenewalLeadDays { get; init; } = 7;
    public int RenewalCheckSeconds { get; init; } = 3600;
    public int AcknowledgmentLeaseSeconds { get; init; } = 30;

    public void Validate()
    {
        if (LeafLifetimeDays is < 2 or > 90 || RenewalLeadDays < 1 || RenewalLeadDays >= LeafLifetimeDays ||
            RenewalCheckSeconds is < 1 or > 86_400 || RenewalCheckSeconds > RenewalLeadDays * 86_400 ||
            AcknowledgmentLeaseSeconds is < 5 or > 300)
            throw new InvalidDataException("Invalid serving certificate or acknowledgment settings.");
    }
}
