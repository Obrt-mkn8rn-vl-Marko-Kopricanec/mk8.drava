using Mk8.Drava.Configuration;

namespace Mk8.Drava.IntegrationTests;

internal sealed record DevelopmentLifecycleSettings
{
    public RegistrationSettings Registration { get; init; } = new();
    public ServingPlanSettings Serving { get; init; } = new();
    public GatewayPlanSettings Gateway { get; init; } = new();

    public static DevelopmentLifecycleSettings Fast => new()
    {
        Registration = new RegistrationSettings { LeaseSeconds = 15, RenewAfterSeconds = 4, ReadinessIntervalMilliseconds = 250,
            ReadinessTimeoutMilliseconds = 200, ReadinessValiditySeconds = 2, ReadinessSuccesses = 1, ReadinessFailures = 1,
            ReconcileIntervalMilliseconds = 100, MaximumConcurrentProbes = 2 },
        Serving = new ServingPlanSettings { LeafLifetimeDays = 7, RenewalLeadDays = 2, AcknowledgmentLeaseSeconds = 6, RenewalCheckSeconds = 60 },
        Gateway = new GatewayPlanSettings { RefreshSeconds = 60, RetrySeconds = 1, RequestDeadlineSeconds = 1, MaximumRetainedGenerations = 2, TlsHandshakeSeconds = 2 },
    };
}

