using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal static class RegistrationPolicyMapping
{
    public static RegistrationRuntimePolicy ToPolicy(RegistrationSettings settings) => new()
    {
        LeaseSeconds = settings.LeaseSeconds, RenewAfterSeconds = settings.RenewAfterSeconds,
        ReconcileIntervalMilliseconds = settings.ReconcileIntervalMilliseconds, ReadinessIntervalMilliseconds = settings.ReadinessIntervalMilliseconds,
        ReadinessTimeoutMilliseconds = settings.ReadinessTimeoutMilliseconds, ReadinessValiditySeconds = settings.ReadinessValiditySeconds,
        ReadinessSuccesses = settings.ReadinessSuccesses, ReadinessFailures = settings.ReadinessFailures,
        MaximumConcurrentProbes = settings.MaximumConcurrentProbes,
    };
}
