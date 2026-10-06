namespace Mk8.Drava.Application.INF.Registry;

public sealed record AuthenticatedEnrollment(string CertificateFingerprint, string NodeId, string OwnerId, DateTimeOffset NotAfterUtc);
