namespace Mk8.Drava.Contracts.Administration.V1;
public sealed record CircuitBreakerStatusResponse(CircuitBreakerRuntimeStateResponse State, bool Enabled, int FailureThreshold, int HalfOpenMaxAttempts, DateTimeOffset? OpenedAtUtc, DateTimeOffset? NextAttemptAtUtc, int FailureCount, long RejectedRequests, string? LastFailureReason)
{
    public static CircuitBreakerStatusResponse Disabled { get; } = new(CircuitBreakerRuntimeStateResponse.Disabled, Enabled: false, FailureThreshold: 5, HalfOpenMaxAttempts: 1, OpenedAtUtc: null, NextAttemptAtUtc: null, FailureCount: 0, RejectedRequests: 0, LastFailureReason: null);
}
