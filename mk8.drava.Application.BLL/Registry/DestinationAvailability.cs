namespace Mk8.Drava.Application.BLL.Registry;

internal sealed class DestinationAvailability
{
    public InstanceIntent? Intent { get; set; }
    public long RenewedAt { get; set; }
    public TimeSpan Lease { get; set; }
    public DateTimeOffset CredentialNotAfterUtc { get; set; }
    public bool Revoked { get; set; }
    public bool Ready { get; set; }
    public long ReadinessGeneration { get; set; }
    public long PublicationAt { get; set; }
    public TimeSpan PublicationValidity { get; set; }
    public long CheckedAt { get; set; }
    public TimeSpan ProofValidity { get; set; }
    public DestinationPublication? Publication { get; set; }
    public int Active { get; set; }

    public bool LeaseValid(TimeProvider clock)
    {
        var age = clock.GetElapsedTime(RenewedAt);
        return Intent is not null && !Revoked && !Intent.Draining && age >= TimeSpan.Zero && age < Lease && clock.GetUtcNow() < CredentialNotAfterUtc;
    }

    public bool IsEligible(TimeProvider clock)
    {
        var age = clock.GetElapsedTime(CheckedAt);
        return LeaseValid(clock) && Ready && age >= TimeSpan.Zero && age < ProofValidity && Publication?.IsValid(clock.GetUtcNow()) == true && clock.GetElapsedTime(PublicationAt) >= TimeSpan.Zero && clock.GetElapsedTime(PublicationAt) < PublicationValidity;
    }
}
