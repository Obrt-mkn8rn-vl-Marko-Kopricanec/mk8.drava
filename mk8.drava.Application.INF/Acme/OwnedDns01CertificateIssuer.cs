using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.Acme;

public sealed class OwnedDns01CertificateIssuer : IAcmeCertificateIssuer, IDisposable
{
    private readonly AcmeDns01CleanupJournal _journal;
    private readonly CloudflareAcmeDns01ChallengeProvider _provider;
    private readonly CertesDns01CertificateIssuer _issuer;

    public OwnedDns01CertificateIssuer(ControllerBootstrap controller, string siteId)
    {
        ArgumentNullException.ThrowIfNull(controller);
        controller.Validate(); RegistrationSiteTrust.RequireLabel(siteId);
        if (!controller.Acme.Enabled) throw new InvalidDataException("Owner public issuance is disabled.");
        _journal = new AcmeDns01CleanupJournal(controller.Acme.CleanupJournalPath);
        CloudflareAcmeDns01ChallengeProvider? provider = null;
        try
        {
            provider = new CloudflareAcmeDns01ChallengeProvider(controller.DnsPublication, controller.Domain, siteId,
                new AcmeDns01PropagationVerifier(controller.DnsServerAddress, controller.DnsServerPort), journal: _journal);
            _issuer = new CertesDns01CertificateIssuer(new AcmeDns01IssuerPolicy
            {
                SiteDomain = controller.Domain, Directory = controller.Acme.DirectoryUrl, AccountKeyPath = controller.Acme.AccountKeyPath,
                ContactEmails = controller.Acme.ContactEmails, TermsAccepted = controller.Acme.TermsAccepted,
                RequestTimeout = TimeSpan.FromSeconds(controller.Acme.RequestTimeoutSeconds), OperationTimeout = TimeSpan.FromSeconds(controller.Acme.OperationTimeoutSeconds),
                PollInterval = TimeSpan.FromSeconds(controller.Acme.PollIntervalSeconds), CleanupTimeout = TimeSpan.FromSeconds(controller.Acme.CleanupTimeoutSeconds),
            }, provider);
            _provider = provider;
        }
        catch { provider?.Dispose(); _journal.Dispose(); throw; }
    }

    public ValueTask<AcmeCertificateIssueResult> IssueAsync(AcmeCertificateIssueRequest request, AcmeChallengeStore challengeStore, CancellationToken cancellationToken) =>
        _issuer.IssueAsync(request, challengeStore, cancellationToken);

    // Host lifetime must join the renewal service before disposing this owned issuer/provider/journal graph.
    public void Dispose() { _issuer.Dispose(); _provider.Dispose(); _journal.Dispose(); }
}
