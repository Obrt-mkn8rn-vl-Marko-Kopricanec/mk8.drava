namespace Mk8.Drava.Contracts.Registration.V1;

public enum RegistrationPhase
{
    Accepted,
    Checking,
    RouteApplied,
    DnsPending,
    CertificatePending,
    Ready,
    Draining,
    Revoked,
    LeaseExpired,
}
