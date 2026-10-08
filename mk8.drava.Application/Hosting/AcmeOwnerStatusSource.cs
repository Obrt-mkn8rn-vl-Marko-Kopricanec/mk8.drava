using Mk8.Drava.Application.BLL.ControlPlane.Acme;

namespace Mk8.Drava.Application.Hosting;

internal sealed class AcmeOwnerStatusSource(AcmeServingLifecycle lifecycle, AcmeCertificateStatusStore statuses)
    : IProxyAcmeStatusConfigurationSource, IProxyAcmeCertificateLifecycleStatusSource
{
    public ProxyAcmeStatusConfigurationSourceReadResult Read()
    {
        var input = ReadInput(); var certificate = input.Certificates[0];
        var active = certificate.ActiveCertificate;
        IReadOnlyList<ProxyAcmeRuntimeCertificateSource> runtime = active is null ? [] :
            [new ProxyAcmeRuntimeCertificateSource(certificate.Id, certificate.Id, "acme", active.NotBeforeUtc, active.NotAfterUtc)];
        return ProxyAcmeStatusConfigurationSourceReadResult.Available(new ProxyAcmeStatusConfigurationSourceSnapshot(input.Enabled, input.DirectoryUrl,
            string.Equals(input.DirectoryUrl, "https://acme-staging-v02.api.letsencrypt.org/directory", StringComparison.Ordinal),
            [new ProxyAcmeConfiguredCertificateStatus(certificate.Id, certificate.Enabled, certificate.Domains, certificate.RenewBeforeDays)], runtime));
    }

    public IReadOnlyList<AcmeCertificateLifecycleStatus> GetLifecycleStatuses()
    {
        var certificate = ReadInput().Certificates[0]; var active = certificate.ActiveCertificate; var history = statuses.Get(certificate.Id);
        DateTimeOffset? due = active is null ? null : AcmeRenewalTiming.CalculateDueAtUtc(active, certificate.RenewBeforeDays, certificate.LifetimeAwareRenewal);
        // Active material comes from the accepted plan even before the first worker check or after a status-write failure.
        return [new AcmeCertificateLifecycleStatus(certificate.Id, certificate.Enabled, certificate.Domains, active is not null, active is null ? "none" : "acme",
            active?.NotBeforeUtc, active?.NotAfterUtc, due, history?.LastAttemptAtUtc, history?.LastSucceededAtUtc, history?.LastFailedAtUtc,
            history?.NextAttemptNotBeforeUtc ?? due, history?.LastResult ?? (active is null ? "inactive" : "loaded"), history?.ErrorSummary)];
    }

    private AcmeRenewalConfigurationInput ReadInput() => lifecycle.ReadInput() is AcmeRenewalConfigurationInputReadResult.AvailableResult available ?
        available.Input : throw new InvalidDataException("Owner ACME runtime input is unavailable.");
}
