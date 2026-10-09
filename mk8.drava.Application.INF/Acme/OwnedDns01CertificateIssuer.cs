using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.BLL.Dns;
using Mk8.Drava.Application.INF.Dns.Management;

namespace Mk8.Drava.Application.INF.Acme;

public sealed class OwnedDns01CertificateIssuer : IAcmeCertificateIssuer, IDisposable
{
    private readonly AcmeDns01CleanupJournal? _journal;
    private readonly IDisposable _provider;
    private readonly CertesDns01CertificateIssuer _issuer;

    public OwnedDns01CertificateIssuer(ControllerBootstrap controller, string siteId) : this(controller, siteId, null) { }

    public OwnedDns01CertificateIssuer(ControllerBootstrap controller, string siteId, IDnsMutationJournal? nativeJournal)
    {
        ArgumentNullException.ThrowIfNull(controller);
        controller.Validate(); RegistrationSiteTrust.RequireLabel(siteId);
        if (!controller.Acme.Enabled) throw new InvalidDataException("Owner public issuance is disabled.");
        if (!string.Equals(controller.DnsPublication.Provider, "mk8.dns", StringComparison.Ordinal)) _journal = new AcmeDns01CleanupJournal(controller.Acme.CleanupJournalPath);
        IDisposable? lifetime = null;
        var transferred = false;
        try
        {
            IAcmeDns01ChallengeProvider provider;
            if (string.Equals(controller.DnsPublication.Provider, "mk8.dns", StringComparison.Ordinal))
            {
                var native = new NativeAcmeDns01ChallengeProvider(controller.Acme.NativeManagement!, controller.Domain, siteId,
                    controller.Acme.NativeDnsTtlSeconds, controller.DnsServerAddress, controller.DnsServerPort,
                    nativeJournal ?? throw new InvalidDataException("Native DNS01 requires the Application's durable journal."));
                provider = native; lifetime = native;
            }
            else
            {
                var cloudflare = new CloudflareAcmeDns01ChallengeProvider(controller.DnsPublication, controller.Domain, siteId,
                    new AcmeDns01PropagationVerifier(controller.DnsServerAddress, controller.DnsServerPort), journal: _journal);
                provider = cloudflare; lifetime = cloudflare;
            }
            _issuer = new CertesDns01CertificateIssuer(new AcmeDns01IssuerPolicy
            {
                SiteDomain = controller.Domain, Directory = controller.Acme.DirectoryUrl, AccountKeyPath = controller.Acme.AccountKeyPath,
                ContactEmails = controller.Acme.ContactEmails, TermsAccepted = controller.Acme.TermsAccepted,
                RequestTimeout = TimeSpan.FromSeconds(controller.Acme.RequestTimeoutSeconds), OperationTimeout = TimeSpan.FromSeconds(controller.Acme.OperationTimeoutSeconds),
                PollInterval = TimeSpan.FromSeconds(controller.Acme.PollIntervalSeconds), CleanupTimeout = TimeSpan.FromSeconds(controller.Acme.CleanupTimeoutSeconds),
            }, provider);
            _provider = lifetime;
            transferred = true;
        }
        finally { if (!transferred) { lifetime?.Dispose(); _journal?.Dispose(); } }
    }

    public ValueTask<AcmeCertificateIssueResult> IssueAsync(AcmeCertificateIssueRequest request, AcmeChallengeStore challengeStore, CancellationToken cancellationToken) =>
        _issuer.IssueAsync(request, challengeStore, cancellationToken);

    // Host lifetime must join the renewal service before disposing this owned issuer/provider/journal graph.
    public void Dispose() { _issuer.Dispose(); _provider.Dispose(); _journal?.Dispose(); }
}
