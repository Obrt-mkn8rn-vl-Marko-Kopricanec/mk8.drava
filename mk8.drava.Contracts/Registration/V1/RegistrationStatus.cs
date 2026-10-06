namespace Mk8.Drava.Contracts.Registration.V1;

public sealed record RegistrationStatus
{
    public RegistrationIdentity Identity { get; init; } = new();
    public RegistrationPhase Phase { get; init; }
    public long DesiredRevision { get; init; }
    public int LeaseSeconds { get; init; }
    public int RenewAfterSeconds { get; init; }
    public IReadOnlyList<string> AssignedUrls { get; init; } = [];
    public string Reason { get; init; } = "";
}
