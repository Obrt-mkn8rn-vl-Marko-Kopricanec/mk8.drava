using Google.Protobuf;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.DAL.Publication;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Certificates;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.Hosting;

internal sealed class ServingPlanState : IGatewayPublicationSource, IDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _renewal = new(1, 1);
    private readonly ApplicationBootstrap _bootstrap;
    private readonly LocalSiteCertificateAuthority _authority;
    private readonly TimeProvider _clock;
    private PresentationPlan _plan;
    private bool _acknowledged;
    private long _acknowledgedAt;
    private int _disposed;

    private ServingPlanState(ApplicationBootstrap bootstrap, LocalSiteCertificateAuthority authority, TimeProvider clock, PresentationPlan plan)
    { _bootstrap = bootstrap; _authority = authority; _clock = clock; _plan = plan; }

    public static async ValueTask<ServingPlanState> OpenAsync(ApplicationBootstrap bootstrap, LocalSiteCertificateAuthority authority, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrap); ArgumentNullException.ThrowIfNull(authority); ArgumentNullException.ThrowIfNull(clock);
        bootstrap.Controller!.ServingPlan.Validate();
        bootstrap.Controller.ServingTrust.Validate();
        bootstrap.Controller.Acme.Validate(bootstrap.Controller.ServingTrust, bootstrap.Controller.DnsPublication);
        var settings = bootstrap.Controller.ServingPlan;
        var stored = GatewayMaterialStore.Read(bootstrap.StateDirectory);
        var prior = stored is null ? null : PresentationPlan.Parser.ParseFrom(stored);
        if (prior is not null)
        {
            var historicalTrust = new ServingTrustSettings { Mode = prior.ServingTrustMode.Length == 0 ? "site-ca" : prior.ServingTrustMode, RootFingerprint = prior.ServingRootFingerprint };
            using var validated = new ValidatedServingPlan(prior, GatewayScope(bootstrap) with { ServingTrust = historicalTrust }, clock, requireCurrent: false);
            if (!string.Equals(prior.Certificates[0].HostNames[0], "*." + bootstrap.Controller!.Domain, StringComparison.Ordinal))
                throw new InvalidDataException("Stored plan belongs to another site domain.");
        }
        var policyChanged = prior is not null && (prior.Certificates.Count != 2 || prior.AcknowledgmentLeaseSeconds != settings.AcknowledgmentLeaseSeconds || prior.LeafLifetimeDays != settings.LeafLifetimeDays ||
            prior.ServingPending && !bootstrap.Controller.Acme.Enabled ||
            !TrustMatches(prior, bootstrap.Controller.ServingTrust) || PublicMaterialChanged(prior, bootstrap.Controller));
        using var issuer = authority.PublicCertificate;
        var issuerUntil = new DateTimeOffset(issuer.NotAfter.ToUniversalTime()).AddMinutes(-5).ToUnixTimeSeconds();
        var retainPublic = prior is { ServingPending: false } && bootstrap.Controller.Acme.Enabled && PrivateCertificateFile.IsAbsent(bootstrap.Controller.ServingCertificatePath) && prior.ValidUntilUnixSeconds > clock.GetUtcNow().ToUnixTimeSeconds();
        var plan = prior is null || policyChanged || (prior.ValidUntilUnixSeconds < issuerUntil && prior.ValidUntilUnixSeconds <= clock.GetUtcNow().AddDays(settings.RenewalLeadDays).ToUnixTimeSeconds())
            && !retainPublic ? BuildPlan(bootstrap, authority, prior is null ? 1 : checked(prior.Generation + 1)) : prior;
        if (prior is not null && !policyChanged && plan.ValidUntilUnixSeconds <= prior.ValidUntilUnixSeconds) plan = prior;
        if (!ReferenceEquals(plan, prior))
        {
            using var validated = new ValidatedServingPlan(plan, GatewayScope(bootstrap), clock, requireCurrent: true);
            await GatewayMaterialStore.WriteAsync(bootstrap.StateDirectory, plan.ToByteArray(), cancellationToken).ConfigureAwait(false);
        }
        return new ServingPlanState(bootstrap, authority, clock, plan);
    }

    public PresentationPlan Read(string gatewayId)
    {
        lock (_gate)
        {
            if (!string.Equals(gatewayId, _plan.GatewayId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Unrecognized Gateway plan identity.");
            return _plan.Clone();
        }
    }

    public bool IsAcknowledged => ReadPublicationProof() is not null;

    public GatewayPublicationProof? ReadPublicationProof()
    {
        lock (_gate)
        {
            var elapsed = _clock.GetElapsedTime(_acknowledgedAt);
            var now = _clock.GetUtcNow();
            var acknowledgmentLease = TimeSpan.FromSeconds(_plan.AcknowledgmentLeaseSeconds);
            if (_plan.ServingPending || !_acknowledged || elapsed < TimeSpan.Zero || elapsed >= acknowledgmentLease || _plan.ValidUntilUnixSeconds <= now.ToUnixTimeSeconds()) return null;
            var valid = DateTimeOffset.FromUnixTimeSeconds(_plan.ValidUntilUnixSeconds);
            var heartbeatUntil = now.Add(acknowledgmentLease - elapsed);
            return new(checked((long)_plan.Generation), valid < heartbeatUntil ? valid : heartbeatUntil, _bootstrap.Controller!.Domain, _bootstrap.HttpPort, _bootstrap.HttpsPort);
        }
    }

    public bool Acknowledge(PlanAcknowledgment acknowledgment)
    {
        ArgumentNullException.ThrowIfNull(acknowledgment);
        lock (_gate)
        {
            if (acknowledgment.Version != 1 || !string.Equals(acknowledgment.GatewayId, _plan.GatewayId, StringComparison.Ordinal) ||
                acknowledgment.Generation != _plan.Generation || !acknowledgment.ContentSha256.Equals(_plan.ContentSha256))
                throw new InvalidDataException("Gateway acknowledgment does not identify the issued plan.");
            _acknowledged = acknowledgment.Applied; _acknowledgedAt = _clock.GetTimestamp();
            return _acknowledged;
        }
    }

    public async ValueTask RenewIfRequiredAsync(CancellationToken cancellationToken)
    {
        await _renewal.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var prior = Read(_bootstrap.GatewayId);
            if (!prior.ServingPending && _bootstrap.Controller!.Acme.Enabled && PrivateCertificateFile.IsAbsent(_bootstrap.Controller.ServingCertificatePath) && prior.ValidUntilUnixSeconds > _clock.GetUtcNow().ToUnixTimeSeconds()) return;
            var publicMaterialChanged = PublicMaterialChanged(prior, _bootstrap.Controller!);
            if (!publicMaterialChanged && prior.ValidUntilUnixSeconds > _clock.GetUtcNow().AddDays(_bootstrap.Controller!.ServingPlan.RenewalLeadDays).ToUnixTimeSeconds()) return;
            using var issuer = _authority.PublicCertificate;
            if (!publicMaterialChanged && new DateTimeOffset(issuer.NotAfter.ToUniversalTime()).AddMinutes(-5).ToUnixTimeSeconds() <= prior.ValidUntilUnixSeconds)
                throw new InvalidOperationException("Site issuer renewal is required to extend the serving certificate.");
            var plan = BuildPlan(_bootstrap, _authority, checked(prior.Generation + 1));
            if (!publicMaterialChanged && plan.ValidUntilUnixSeconds <= prior.ValidUntilUnixSeconds)
                throw new InvalidOperationException("Site issuer renewal is required to extend the serving certificate.");
            using var validated = new ValidatedServingPlan(plan, GatewayScope(_bootstrap), _clock, requireCurrent: true);
            await GatewayMaterialStore.WriteAsync(_bootstrap.StateDirectory, plan.ToByteArray(), cancellationToken).ConfigureAwait(false);
            lock (_gate) { _plan = plan; _acknowledged = false; }
        }
        finally { _renewal.Release(); }
    }

    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _renewal.Dispose(); }

    private static GatewayBootstrap GatewayScope(ApplicationBootstrap bootstrap) => new()
    {
        SiteId = bootstrap.SiteId, GatewayId = bootstrap.GatewayId, BindAddress = bootstrap.IngressAddress,
        HttpPort = bootstrap.HttpPort, HttpsPort = bootstrap.HttpsPort, ManagementPort = bootstrap.ManagementPort,
        RegistrationPort = bootstrap.Controller!.RegistrationPort, EnrollmentRootFingerprint = bootstrap.Controller.EnrollmentRootFingerprint,
        ServingTrust = bootstrap.Controller.ServingTrust,
    };

    private static bool TrustMatches(PresentationPlan plan, ServingTrustSettings trust) =>
        string.Equals(plan.ServingTrustMode.Length == 0 ? "site-ca" : plan.ServingTrustMode, trust.Mode, StringComparison.Ordinal) &&
        string.Equals(plan.ServingRootFingerprint, trust.RootFingerprint, StringComparison.Ordinal);

    private static bool PublicMaterialChanged(PresentationPlan plan, ControllerBootstrap controller)
    {
        if (string.Equals(controller.ServingTrust.Mode, "site-ca", StringComparison.Ordinal)) return false;
        if (controller.Acme.Enabled && PrivateCertificateFile.IsAbsent(controller.ServingCertificatePath)) return false;
        return plan.ServingPending || !plan.Certificates[0].Pfx.Span.SequenceEqual(PrivateCertificateFile.Read(controller.ServingCertificatePath));
    }

    private static PresentationPlan BuildPlan(ApplicationBootstrap bootstrap, LocalSiteCertificateAuthority authority, ulong generation)
    {
        var controller = bootstrap.Controller ?? throw new InvalidOperationException("Controller configuration is missing.");
        var pending = controller.Acme.Enabled && PrivateCertificateFile.IsAbsent(controller.ServingCertificatePath);
        var serving = pending ? new ServingCertificate { CertificateId = "site", HostNames = { "*." + controller.Domain, "register." + controller.Domain } } : ReadServingCertificate(bootstrap, authority);
        using var enrollment = authority.IssueEnrollmentGateway(controller.Domain, [bootstrap.IngressAddress], controller.ServingPlan.LeafLifetimeDays);
        using var root = authority.PublicCertificate;
        var plan = new PresentationPlan
        {
            Version = 1, SiteId = bootstrap.SiteId, GatewayId = bootstrap.GatewayId, Generation = generation,
            AcknowledgmentLeaseSeconds = checked((uint)controller.ServingPlan.AcknowledgmentLeaseSeconds), LeafLifetimeDays = checked((uint)controller.ServingPlan.LeafLifetimeDays),
            EnrollmentCaDer = ByteString.CopyFrom(root.RawData), ValidUntilUnixSeconds = pending ? new DateTimeOffset(enrollment.NotAfter.ToUniversalTime()).ToUnixTimeSeconds() : Math.Min(serving.NotAfterUnixSeconds, new DateTimeOffset(enrollment.NotAfter.ToUniversalTime()).ToUnixTimeSeconds()),
            ServingTrustMode = controller.ServingTrust.Mode, ServingRootFingerprint = controller.ServingTrust.RootFingerprint,
            ServingPending = pending,
        };
        plan.Certificates.Add(serving);
        plan.Certificates.Add(new ServingCertificate { CertificateId = "enrollment", Pfx = ByteString.CopyFrom(enrollment.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12)),
            NotAfterUnixSeconds = new DateTimeOffset(enrollment.NotAfter.ToUniversalTime()).ToUnixTimeSeconds(), HostNames = { "register." + controller.Domain, "admin." + controller.Domain } });
        if (bootstrap.HttpPort > 0) plan.Listeners.Add(Listener("http", bootstrap.IngressAddress, bootstrap.HttpPort, tls: false, registration: false));
        if (bootstrap.HttpsPort > 0) plan.Listeners.Add(Listener("https", bootstrap.IngressAddress, bootstrap.HttpsPort, tls: true, registration: false));
        plan.Listeners.Add(Listener("registration", bootstrap.IngressAddress, controller.RegistrationPort, tls: true, registration: true));
        if (bootstrap.ManagementPort > 0) plan.Listeners.Add(Listener("management", bootstrap.IngressAddress, bootstrap.ManagementPort, tls: true, registration: false));
        plan.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(plan));
        return plan;
    }

    private static ServingCertificate ReadServingCertificate(ApplicationBootstrap bootstrap, LocalSiteCertificateAuthority authority)
    {
        var controller = bootstrap.Controller!;
        byte[] bytes;
        long notAfter;
        if (string.Equals(controller.ServingTrust.Mode, "site-ca", StringComparison.Ordinal))
        {
            using var certificate = authority.IssueGateway(controller.Domain, [bootstrap.IngressAddress], controller.ServingPlan.LeafLifetimeDays);
            bytes = certificate.Export(X509ContentType.Pkcs12);
            notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).ToUnixTimeSeconds();
        }
        else
        {
            bytes = PrivateCertificateFile.Read(controller.ServingCertificatePath);
            using var certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.EphemeralKeySet, new Pkcs12LoaderLimits { MaxCertificates = 16, MaxKeys = 1 });
            notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).ToUnixTimeSeconds();
        }
        return new ServingCertificate { CertificateId = "site", Pfx = ByteString.CopyFrom(bytes), NotAfterUnixSeconds = notAfter,
            HostNames = { "*." + controller.Domain, "register." + controller.Domain } };
    }

    private static PresentationListener Listener(string id, string address, int port, bool tls, bool registration)
    {
        var listener = new PresentationListener { Id = id, Address = address, Port = (uint)port, Tls = tls, Registration = registration };
        listener.Protocols.Add("http1");
        if (tls) listener.Protocols.Add("http2");
        return listener;
    }
}
