using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Transport.Registration;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.Application.INF.Registry;

public sealed class SignedRegistrationHandler
{
    private readonly string _siteId;
    private readonly RegistryCoordinator _registry;
    private readonly DestinationAvailabilityStore _availability;
    private readonly EnrollmentVerifier _enrollments;
    private readonly EnrollmentChallenges _challenges;
    private readonly TimeProvider _clock;
    private readonly IGatewayPublicationSource? _publication;
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(90);

    public SignedRegistrationHandler(string siteId, RegistryCoordinator registry, DestinationAvailabilityStore availability,
        EnrollmentVerifier enrollments, EnrollmentChallenges challenges, TimeProvider clock, IGatewayPublicationSource? publication = null)
    {
        RegistryNames.RequireLabel(siteId);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(enrollments);
        ArgumentNullException.ThrowIfNull(challenges);
        ArgumentNullException.ThrowIfNull(clock);
        _siteId = siteId;
        _registry = registry;
        _availability = availability;
        _enrollments = enrollments;
        _challenges = challenges;
        _clock = clock;
        _publication = publication;
    }

    public ChallengeReply Challenge(ChallengeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != 1) throw new InvalidDataException("Unsupported registration version.");
        var enrollment = _enrollments.Authenticate(request.EnrollmentCertificateDer.Span);
        return new ChallengeReply
        {
            Version = 1, SiteId = _siteId,
            Nonce = ByteString.CopyFrom(_challenges.Create(enrollment.CertificateFingerprint)),
            ExpiresUnixSeconds = _clock.GetUtcNow().AddSeconds(30).ToUnixTimeSeconds(),
        };
    }

    public async ValueTask<RegistrationStatus> SubmitAsync(SignedCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != 1 || request.Nonce.Length != 32 || request.JsonPayload.Length is < 1 or > RegistrationProof.MaximumPayloadBytes)
            throw new InvalidDataException("Unsupported registration version or bounds.");
        var enrollment = _enrollments.Authenticate(request.EnrollmentCertificateDer.Span);
        using var certificate = X509CertificateLoader.LoadCertificate(request.EnrollmentCertificateDer.Span);
        using var key = certificate.GetECDsaPublicKey() ?? throw new UnauthorizedAccessException("Invalid enrollment signing key.");
        if (!RegistrationProof.Verify(key, _siteId, request.Version, request.Nonce.Span, request.JsonPayload.Span, request.Signature.Span) ||
            !_challenges.Consume(enrollment.CertificateFingerprint, request.Nonce.Span))
            throw new UnauthorizedAccessException("Registration proof is invalid, expired or replayed.");
        var command = RegistrationJson.Decode(request.JsonPayload.Memory);
        if (command.Identity is null) throw new InvalidDataException("Registration identity is missing.");
        if (command.Version != 1 || !Enum.IsDefined(command.Operation) || command.Operation == RegistrationOperation.Unspecified ||
            !string.Equals(command.Identity.SiteId, _siteId, StringComparison.Ordinal) ||
            !string.Equals(command.Identity.NodeId, enrollment.NodeId, StringComparison.Ordinal) ||
            !string.Equals(command.Identity.OwnerId, enrollment.OwnerId, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Registration identity does not match its enrollment.");
        var identity = ToDomain(command.Identity);
        if (command.Operation == RegistrationOperation.Register)
        {
            var metadata = command.Advertisement ?? throw new InvalidDataException("Register requires the bound endpoint metadata.");
            var intent = new InstanceIntent(identity, metadata.DeploymentId, metadata.Address, metadata.Port, metadata.Protocol,
                metadata.Scheme, metadata.ReadinessPath, metadata.Zone, metadata.Weight, draining: false);
            await _registry.RegisterAsync(enrollment.CertificateFingerprint, intent, Lease, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (command.Advertisement is not null) throw new InvalidDataException("Only register can supply endpoint metadata.");
            if (command.Operation == RegistrationOperation.Renew)
                await _registry.RenewAsync(enrollment.CertificateFingerprint, identity, Lease, cancellationToken).ConfigureAwait(false);
            else if (command.Operation == RegistrationOperation.Drain)
                await _registry.DrainAsync(enrollment.CertificateFingerprint, identity, cancellationToken).ConfigureAwait(false);
        }
        if (!_registry.State.Instances.TryGetValue(identity.InstanceId, out var current) || current.Identity != identity)
            throw new InvalidDataException("Unknown or superseded instance boot.");
        var status = _availability.Status(identity);
        var phase = current.Draining ? RegistrationPhase.Draining : status.Revoked ? RegistrationPhase.Revoked : !status.LeaseValid ? RegistrationPhase.LeaseExpired :
            !status.ReadinessValid ? RegistrationPhase.Checking : status.PublicationValid ? RegistrationPhase.Ready :
            status.Publication is { RouteRevision: > 0, CertificateVerified: false } ? RegistrationPhase.CertificatePending :
            status.Publication is { RouteRevision: > 0, DnsVerified: false } ? RegistrationPhase.DnsPending : RegistrationPhase.Checking;
        return new RegistrationStatus
        {
            Identity = command.Identity, Phase = phase, DesiredRevision = _registry.State.Revision,
            LeaseSeconds = 90, RenewAfterSeconds = 30,
            AssignedUrls = phase == RegistrationPhase.Ready ? AssignedUrls(identity) : [],
            Reason = phase == RegistrationPhase.Checking ? "Readiness and acknowledged publication are required." : "",
        };
    }

    private IReadOnlyList<string> AssignedUrls(RegisteredUpstreamIdentity identity)
    {
        var gateway = _publication?.ReadPublicationProof();
        if (gateway is null) return [];
        var host = identity.ServiceId + "." + gateway.Domain;
        return gateway.HttpsPort > 0 ? ["https://" + host + (gateway.HttpsPort == 443 ? "" : ":" + gateway.HttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "/"] :
            gateway.HttpPort > 0 ? ["http://" + host + (gateway.HttpPort == 80 ? "" : ":" + gateway.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "/"] : [];
    }

    private static RegisteredUpstreamIdentity ToDomain(RegistrationIdentity identity) => new(identity.NodeId, identity.OwnerId, identity.ServiceId,
        identity.ContractId, identity.InstanceId, identity.BootId);
}
