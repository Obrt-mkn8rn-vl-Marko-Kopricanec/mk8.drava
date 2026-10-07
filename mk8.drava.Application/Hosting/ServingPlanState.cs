using Google.Protobuf;
using Mk8.Drava.Application.BLL.Registry;
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
        var settings = bootstrap.Controller.ServingPlan;
        var stored = GatewayMaterialStore.Read(bootstrap.StateDirectory);
        var prior = stored is null ? null : PresentationPlan.Parser.ParseFrom(stored);
        if (prior is not null)
        {
            using var validated = new ValidatedServingPlan(prior, GatewayScope(bootstrap), clock, requireCurrent: false);
            if (!string.Equals(prior.Certificates[0].HostNames[0], "*." + bootstrap.Controller!.Domain, StringComparison.Ordinal))
                throw new InvalidDataException("Stored plan belongs to another site domain.");
        }
        var plan = prior is null || prior.AcknowledgmentLeaseSeconds != settings.AcknowledgmentLeaseSeconds || prior.LeafLifetimeDays != settings.LeafLifetimeDays ||
            prior.ValidUntilUnixSeconds <= clock.GetUtcNow().AddDays(settings.RenewalLeadDays).ToUnixTimeSeconds()
            ? BuildPlan(bootstrap, authority, prior is null ? 1 : checked(prior.Generation + 1)) : prior;
        if (!ReferenceEquals(plan, prior)) await GatewayMaterialStore.WriteAsync(bootstrap.StateDirectory, plan.ToByteArray(), cancellationToken).ConfigureAwait(false);
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
            if (!_acknowledged || elapsed < TimeSpan.Zero || elapsed >= acknowledgmentLease || _plan.ValidUntilUnixSeconds <= now.ToUnixTimeSeconds()) return null;
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
            if (prior.ValidUntilUnixSeconds > _clock.GetUtcNow().AddDays(_bootstrap.Controller!.ServingPlan.RenewalLeadDays).ToUnixTimeSeconds()) return;
            var plan = BuildPlan(_bootstrap, _authority, checked(prior.Generation + 1));
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
    };

    private static PresentationPlan BuildPlan(ApplicationBootstrap bootstrap, LocalSiteCertificateAuthority authority, ulong generation)
    {
        var controller = bootstrap.Controller ?? throw new InvalidOperationException("Controller configuration is missing.");
        using var certificate = authority.IssueGateway(controller.Domain, [bootstrap.IngressAddress], controller.ServingPlan.LeafLifetimeDays);
        using var root = authority.PublicCertificate;
        var plan = new PresentationPlan
        {
            Version = 1, SiteId = bootstrap.SiteId, GatewayId = bootstrap.GatewayId, Generation = generation,
            AcknowledgmentLeaseSeconds = checked((uint)controller.ServingPlan.AcknowledgmentLeaseSeconds), LeafLifetimeDays = checked((uint)controller.ServingPlan.LeafLifetimeDays),
            EnrollmentCaDer = ByteString.CopyFrom(root.RawData), ValidUntilUnixSeconds = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).ToUnixTimeSeconds(),
        };
        plan.Certificates.Add(new ServingCertificate { CertificateId = "site", Pfx = ByteString.CopyFrom(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12)),
            NotAfterUnixSeconds = plan.ValidUntilUnixSeconds, HostNames = { "*." + controller.Domain, "register." + controller.Domain } });
        if (bootstrap.HttpPort > 0) plan.Listeners.Add(Listener("http", bootstrap.IngressAddress, bootstrap.HttpPort, tls: false, registration: false));
        if (bootstrap.HttpsPort > 0) plan.Listeners.Add(Listener("https", bootstrap.IngressAddress, bootstrap.HttpsPort, tls: true, registration: false));
        plan.Listeners.Add(Listener("registration", bootstrap.IngressAddress, controller.RegistrationPort, tls: true, registration: true));
        if (bootstrap.ManagementPort > 0) plan.Listeners.Add(Listener("management", bootstrap.IngressAddress, bootstrap.ManagementPort, tls: true, registration: false));
        plan.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(plan));
        return plan;
    }

    private static PresentationListener Listener(string id, string address, int port, bool tls, bool registration)
    {
        var listener = new PresentationListener { Id = id, Address = address, Port = (uint)port, Tls = tls, Registration = registration };
        listener.Protocols.Add("http1");
        if (tls) listener.Protocols.Add("http2");
        return listener;
    }
}
