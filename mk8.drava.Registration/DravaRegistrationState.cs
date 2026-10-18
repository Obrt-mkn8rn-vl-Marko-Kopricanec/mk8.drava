using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Registration;

public sealed class DravaRegistrationState
{
    private readonly TimeProvider _clock;
    private Observation _observation = new(Status: null, Failure: "", Timestamp: 0, Stopping: false);

    public DravaRegistrationState() : this(TimeProvider.System) { }
    internal DravaRegistrationState(TimeProvider clock) => _clock = clock;

    public RegistrationStatus? Status => ReadCurrent().Status;
    public string Failure => ReadCurrent().Failure;
    internal void Accept(RegistrationStatus status)
    {
        var snapshot = status with { AssignedUrls = Array.AsReadOnly(status.AssignedUrls.ToArray()) };
        while (true)
        {
            var observed = Volatile.Read(ref _observation);
            if (observed.Stopping && snapshot.Phase != RegistrationPhase.Draining) return;
            var accepted = new Observation(snapshot, "", _clock.GetTimestamp(), observed.Stopping);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _observation, accepted, observed), observed)) return;
        }
    }

    internal void Failed() => UpdateFailure("Enrolled Gateway discovery or registration is unavailable.", stopping: false);
    internal void BeginStopping() => UpdateFailure("Registration is stopping; ready membership is no longer advertised.", stopping: true);

    private void UpdateFailure(string failure, bool stopping)
    {
        while (true)
        {
            var observed = Volatile.Read(ref _observation);
            if (stopping && observed.Stopping) return;
            var failed = new Observation(Status: null, failure, _clock.GetTimestamp(), stopping || observed.Stopping);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _observation, failed, observed), observed)) return;
        }
    }

    private Observation ReadCurrent()
    {
        var observed = Volatile.Read(ref _observation);
        return observed.Status is { } status && _clock.GetElapsedTime(observed.Timestamp) >= TimeSpan.FromSeconds(status.LeaseSeconds)
            ? new Observation(Status: null, "The registration observation expired; a fresh enrolled Gateway response is required.", observed.Timestamp, observed.Stopping)
            : observed;
    }

    private sealed record Observation(RegistrationStatus? Status, string Failure, long Timestamp, bool Stopping);
}
