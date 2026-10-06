using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
public sealed record AcmeRenewalActiveCertificate(DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc);
