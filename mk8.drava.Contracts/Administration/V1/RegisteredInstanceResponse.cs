using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class RegisteredInstanceResponse
{
    public RegistrationIdentity Identity { get; init; } = new();
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public string Protocol { get; init; } = "";
    public string Scheme { get; init; } = "";
    public bool Draining { get; init; }
    public bool LeaseValid { get; init; }
    public bool ReadinessValid { get; init; }
    public bool PublicationValid { get; init; }
    public bool Revoked { get; init; }
}
