using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Certificates;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Presentation.Certificates;

public sealed class GatewayServingMaterial : IDisposable
{
    private readonly ValidatedServingPlan _material;
    public X509Certificate2 ServingCertificate => _material.ServingCertificate;
    public X509Certificate2 EnrollmentCertificate => _material.EnrollmentCertificate;
    public SslStreamCertificateContext ServingContext => _material.ServingContext;
    public SslStreamCertificateContext EnrollmentContext => _material.EnrollmentContext;
    public PresentationPlan Plan => _material.Plan;
    public GatewayServingMaterial(PresentationPlan plan, GatewayBootstrap bootstrap) : this(plan, bootstrap, requireCurrent: true) { }
    private GatewayServingMaterial(PresentationPlan plan, GatewayBootstrap bootstrap, bool requireCurrent) => _material = new ValidatedServingPlan(plan, bootstrap, TimeProvider.System, requireCurrent);
    public static GatewayServingMaterial Restore(PresentationPlan plan, GatewayBootstrap bootstrap) => new(plan, bootstrap, requireCurrent: false);
    public bool ValidateClientCertificate(X509Certificate2 certificate) => _material.ValidateClientCertificate(certificate);
    public void Dispose() => _material.Dispose();
}
