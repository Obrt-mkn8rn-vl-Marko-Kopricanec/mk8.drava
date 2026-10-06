using Google.Protobuf;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.Hosting;

internal sealed class ServingPlanState
{
    private readonly PresentationPlan _plan;
    private int _acknowledged;

    public ServingPlanState(ApplicationBootstrap bootstrap, LocalSiteCertificateAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(authority);
        var controller = bootstrap.Controller ?? throw new InvalidOperationException("Controller configuration is missing.");
        using var certificate = authority.IssueGateway(controller.Domain, [bootstrap.IngressAddress]);
        using var root = authority.PublicCertificate;
        _plan = new PresentationPlan
        {
            Version = 1, SiteId = bootstrap.SiteId, GatewayId = bootstrap.GatewayId, Generation = 1,
            EnrollmentCaDer = ByteString.CopyFrom(root.RawData),
            ValidUntilUnixSeconds = new DateTimeOffset(certificate.NotAfter.ToUniversalTime()).ToUnixTimeSeconds(),
        };
        _plan.Certificates.Add(new ServingCertificate
        {
            CertificateId = "site", Pfx = ByteString.CopyFrom(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12)),
            NotAfterUnixSeconds = _plan.ValidUntilUnixSeconds,
            HostNames = { "*." + controller.Domain, "register." + controller.Domain },
        });
        if (bootstrap.HttpPort > 0) _plan.Listeners.Add(Listener("http", bootstrap.IngressAddress, bootstrap.HttpPort, tls: false, registration: false));
        if (bootstrap.HttpsPort > 0) _plan.Listeners.Add(Listener("https", bootstrap.IngressAddress, bootstrap.HttpsPort, tls: true, registration: false));
        _plan.Listeners.Add(Listener("registration", bootstrap.IngressAddress, controller.RegistrationPort, tls: true, registration: true));
        _plan.ContentSha256 = ByteString.CopyFrom(PresentationPlanDigest.Compute(_plan));
    }

    public PresentationPlan Read(string gatewayId)
    {
        if (!string.Equals(gatewayId, _plan.GatewayId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Unrecognized Gateway plan identity.");
        return _plan.Clone();
    }

    public bool IsAcknowledged => Volatile.Read(ref _acknowledged) != 0;

    public bool Acknowledge(PlanAcknowledgment acknowledgment)
    {
        ArgumentNullException.ThrowIfNull(acknowledgment);
        if (acknowledgment.Version != 1 || !string.Equals(acknowledgment.GatewayId, _plan.GatewayId, StringComparison.Ordinal) ||
            acknowledgment.Generation != _plan.Generation || !acknowledgment.ContentSha256.Equals(_plan.ContentSha256))
            throw new InvalidDataException("Gateway acknowledgment does not identify the issued plan.");
        Volatile.Write(ref _acknowledged, acknowledgment.Applied ? 1 : 0);
        return IsAcknowledged;
    }

    private static PresentationListener Listener(string id, string address, int port, bool tls, bool registration)
    {
        var listener = new PresentationListener { Id = id, Address = address, Port = (uint)port, Tls = tls, Registration = registration };
        listener.Protocols.Add("http1");
        if (tls) listener.Protocols.Add("http2");
        return listener;
    }
}
