using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Application.INF.Registry;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.INF.NodeRelay;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.DAL.Acme;
using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.Application.Hosting;

internal sealed class RegistrationRuntime : IAsyncDisposable
{
    private readonly SqliteRegistryRepository _repository;
    private readonly LocalSiteCertificateAuthority _authority;
    private readonly EnrollmentVerifier _verifier;
    private readonly X509Certificate2 _relayRoot;
    private readonly X509Certificate2 _relayController;
    public RegisteredRelayConnector Relay { get; }
    public RegistryCoordinator Registry { get; }
    public SignedRegistrationHandler Handler { get; }
    public ServingPlanState Plans { get; }
    public IPolicyRepository Policies => _repository;
    public Mk8.Drava.Application.BLL.Dns.IDnsMutationJournal DnsJournal => _repository;
    public IAcmeCertificateStatusPersistence AcmeHistory(string domain, Uri directory) => new SqliteAcmeCertificateStatusPersistence(_repository, domain, directory);

    private RegistrationRuntime(SqliteRegistryRepository repository, LocalSiteCertificateAuthority authority, RegistryCoordinator registry,
        EnrollmentVerifier verifier, SignedRegistrationHandler handler, ServingPlanState plans, RegisteredRelayConnector relay, X509Certificate2 relayRoot, X509Certificate2 relayController)
    {
        _repository = repository;
        _authority = authority;
        Registry = registry;
        _verifier = verifier;
        Handler = handler;
        Plans = plans;
        Relay = relay; _relayRoot = relayRoot; _relayController = relayController;
    }

    public static async ValueTask<RegistrationRuntime> OpenAsync(ApplicationBootstrap bootstrap, DestinationAvailabilityStore availability,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(clock);
        var controller = bootstrap.Controller ?? throw new InvalidOperationException("Controller configuration is missing.");
        SqliteRegistryRepository? repository = await SqliteRegistryRepository.OpenAsync(bootstrap.StateDirectory, bootstrap.SiteId, cancellationToken).ConfigureAwait(false);
        LocalSiteCertificateAuthority? authority = null;
        RegistryCoordinator? registry = null;
        EnrollmentVerifier? verifier = null;
        RegisteredRelayConnector? relay = null;
        X509Certificate2? relayRoot = null;
        X509Certificate2? relayController = null;
        ServingPlanState? plans = null;
        try
        {
            authority = LocalSiteCertificateAuthority.Open(controller.CertificateAuthorityPath, controller.EnrollmentRootFingerprint, clock);
            registry = new RegistryCoordinator(repository, availability, clock);
            await registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            using var root = authority.PublicCertificate;
            verifier = new EnrollmentVerifier(root, registry, clock);
            plans = await ServingPlanState.OpenAsync(bootstrap, authority, clock, cancellationToken).ConfigureAwait(false);
            var handler = new SignedRegistrationHandler(bootstrap.SiteId, registry, availability, verifier, new EnrollmentChallenges(clock), clock, plans, RegistrationPolicyMapping.ToPolicy(controller.Registration));
            relayRoot = authority.PublicCertificate;
            relayController = authority.IssueController(bootstrap.SiteId, Guid.NewGuid().ToString("N"));
            relay = new RegisteredRelayConnector(bootstrap.SiteId, bootstrap.NodeId, registry, availability, relayController, relayRoot, clock,
                RelayPolicyMapping.ToPolicy(controller.Relay));
            var runtime = new RegistrationRuntime(repository, authority, registry, verifier, handler, plans, relay, relayRoot, relayController);
            (repository, authority, registry, verifier, relay, relayRoot, relayController, plans) = (null, null, null, null, null, null, null, null);
            return runtime;
        }
        finally
        {
            verifier?.Dispose();
            plans?.Dispose();
            if (relay is not null) await relay.DisposeAsync().ConfigureAwait(false);
            relayController?.Dispose(); relayRoot?.Dispose();
            registry?.Dispose();
            authority?.Dispose();
            if (repository is not null) await repository.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Relay.DisposeAsync().ConfigureAwait(false);
        _relayController.Dispose(); _relayRoot.Dispose();
        _verifier.Dispose();
        Registry.Dispose();
        Plans.Dispose();
        _authority.Dispose();
        await _repository.DisposeAsync().ConfigureAwait(false);
    }
}
