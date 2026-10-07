namespace Mk8.Drava.Application.BLL.Registry;

public sealed record RegistrationRuntimePolicy
{
    public int LeaseSeconds { get; init; } = 90;
    public int RenewAfterSeconds { get; init; } = 30;
    public int ReconcileIntervalMilliseconds { get; init; } = 1000;
    public int ReadinessIntervalMilliseconds { get; init; } = 10_000;
    public int ReadinessTimeoutMilliseconds { get; init; } = 2000;
    public int ReadinessValiditySeconds { get; init; } = 30;
    public int ReadinessSuccesses { get; init; } = 2;
    public int ReadinessFailures { get; init; } = 3;
    public int MaximumConcurrentProbes { get; init; } = 32;

    public void Validate()
    {
        DestinationAvailabilityStore.ValidateLease(TimeSpan.FromSeconds(LeaseSeconds));
        if (RenewAfterSeconds is < 1 or > 60 || RenewAfterSeconds > LeaseSeconds / 3 || ReconcileIntervalMilliseconds is < 100 or > 60_000 ||
            ReadinessIntervalMilliseconds is < 100 or > 60_000 || ReadinessTimeoutMilliseconds is < 100 or > 5000 ||
            ReadinessValiditySeconds is < 1 or > 300 || ReadinessValiditySeconds * 1000 < ReadinessIntervalMilliseconds + ReadinessTimeoutMilliseconds ||
            ReadinessSuccesses is < 1 or > 20 || ReadinessFailures is < 1 or > 20 || MaximumConcurrentProbes is < 1 or > 256)
            throw new InvalidDataException("Invalid registration runtime policy.");
    }
}
