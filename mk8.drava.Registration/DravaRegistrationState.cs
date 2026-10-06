using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Registration;

public sealed class DravaRegistrationState
{
    private readonly TimeProvider _clock;
    private Observation _observation = new(null, "", 0);

    public DravaRegistrationState() : this(TimeProvider.System) { }
    internal DravaRegistrationState(TimeProvider clock) => _clock = clock;

    public RegistrationStatus? Status => ReadCurrent().Status;
    public string Failure => ReadCurrent().Failure;
    internal void Accept(RegistrationStatus status) => Volatile.Write(ref _observation, new Observation(status, "", _clock.GetTimestamp()));
    internal void Failed() => Volatile.Write(ref _observation, new Observation(null, "Enrolled Gateway discovery or registration is unavailable.", _clock.GetTimestamp()));

    private Observation ReadCurrent()
    {
        var observed = Volatile.Read(ref _observation);
        return observed.Status is { } status && _clock.GetElapsedTime(observed.Timestamp) >= TimeSpan.FromSeconds(status.LeaseSeconds)
            ? new Observation(null, "The registration observation expired; a fresh enrolled Gateway response is required.", observed.Timestamp)
            : observed;
    }

    private sealed record Observation(RegistrationStatus? Status, string Failure, long Timestamp);
}
