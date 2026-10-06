namespace Mk8.Drava.Application.BLL.Registry;

public sealed record DestinationStatus(bool LeaseValid, bool ReadinessValid, bool PublicationValid, bool Revoked);
