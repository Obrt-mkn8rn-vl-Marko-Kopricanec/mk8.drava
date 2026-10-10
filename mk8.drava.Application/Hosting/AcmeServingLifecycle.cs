using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal sealed class AcmeServingLifecycle(ApplicationBootstrap bootstrap, ServingPlanState plans) : IAcmeRenewalConfigurationSource,
    IAcmeRenewalScheduleInputSource, IAcmeCertificateMaterialWriter, IAcmeCertificateActivator
{
    private readonly Lock _gate = new();
    private PendingMaterial? _pending;
    public AcmeRenewalConfigurationInputReadResult ReadInput()
    {
        var controller = bootstrap.Controller!;
        var plan = plans.Read(bootstrap.GatewayId);
        AcmeRenewalActiveCertificate? active = null;
        if (!plan.ServingPending)
        {
            using var certificate = X509CertificateLoader.LoadPkcs12(plan.Certificates[0].Pfx.Span, null, X509KeyStorageFlags.EphemeralKeySet,
                new Pkcs12LoaderLimits { MaxCertificates = 16, MaxKeys = 1 });
            active = new AcmeRenewalActiveCertificate(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), new DateTimeOffset(certificate.NotAfter.ToUniversalTime()));
        }
        return AcmeRenewalConfigurationInputReadResult.Available(new AcmeRenewalConfigurationInput(true, "owner-serving", controller.Acme.RequireDirectoryUrl().AbsoluteUri,
            controller.Acme.ContactEmails, controller.Acme.TermsAccepted, 5,
            [new AcmeRenewalCertificateInput("site", true, [controller.Domain, "*." + controller.Domain], 30, active, LifetimeAwareRenewal: true)],
            TimeSpan.FromSeconds(controller.Acme.RetryAfterSeconds)));
    }

    AcmeRenewalScheduleInputReadResult IAcmeRenewalScheduleInputSource.ReadInput() => AcmeRenewalScheduleInputReadResult.Available(
        new AcmeRenewalScheduleInput(true, 1, TimeSpan.FromSeconds(bootstrap.Controller!.Acme.CheckIntervalSeconds)));

    public void EnsureLayout(string dataDirectory, string storagePath)
    {
        if (!string.Equals(dataDirectory, bootstrap.StateDirectory, StringComparison.Ordinal) || !string.Equals(storagePath, "owner-serving", StringComparison.Ordinal))
            throw new InvalidDataException("Owner serving lifecycle does not use imported arbitrary material paths.");
    }

    public RuntimeCertificate WriteAndLoad(AcmeCertificateMaterialWriteRequest request) => throw new InvalidOperationException("Owner serving publication requires its awaited writer.");
    public ValueTask<RuntimeCertificate> WriteAndLoadAsync(AcmeCertificateMaterialWriteRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.CertificateId, "site", StringComparison.Ordinal) || !string.Equals(request.StoragePath, "owner-serving", StringComparison.Ordinal) ||
            !string.Equals(request.DataDirectory, bootstrap.StateDirectory, StringComparison.Ordinal) || request.Domains.Count != 2 ||
            !string.Equals(request.Domains[0], bootstrap.Controller!.Domain, StringComparison.Ordinal) || !string.Equals(request.Domains[1], "*." + bootstrap.Controller.Domain, StringComparison.Ordinal))
            throw new InvalidDataException("Owner serving material request changed its approved scope.");
        var certificate = X509CertificateLoader.LoadPkcs12(request.PfxBytes, null, X509KeyStorageFlags.EphemeralKeySet,
            new Pkcs12LoaderLimits { MaxCertificates = 16, MaxKeys = 1 });
        try
        {
            var material = RuntimeCertificateFactory.Acme("site", certificate, request.Domains);
            lock (_gate)
            {
                if (_pending is not null) throw new InvalidOperationException("Owner serving material awaits activation.");
                _pending = new PendingMaterial(material, request.PfxBytes);
            }
            return ValueTask.FromResult(material);
        }
        catch { certificate.Dispose(); throw; }
    }

    public void Activate(RuntimeCertificate certificate) => throw new InvalidOperationException("Owner serving activation requires its awaited publisher.");
    public async ValueTask ActivateAsync(RuntimeCertificate certificate, CancellationToken cancellationToken)
    {
        if (!string.Equals(certificate.Id, "site", StringComparison.Ordinal)) throw new InvalidDataException("Unknown owner serving certificate.");
        PendingMaterial pending;
        lock (_gate) pending = _pending is { } current && ReferenceEquals(current.Certificate, certificate) ? current : throw new InvalidDataException("Owner serving activation requires its exact issued material capability.");
        try
        {
            await plans.PublishIssuedCertificateAsync(pending.Pfx, cancellationToken).ConfigureAwait(false);
            certificate.Certificate.Dispose();
        }
        finally { lock (_gate) _pending = null; }
    }

    private sealed record PendingMaterial(RuntimeCertificate Certificate, byte[] Pfx);
}
