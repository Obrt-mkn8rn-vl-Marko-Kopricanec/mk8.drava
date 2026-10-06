using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Application.INF.Registry;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Hosting;

internal sealed class RegistrationRuntime : IAsyncDisposable
{
    private readonly SqliteRegistryRepository _repository;
    private readonly LocalSiteCertificateAuthority _authority;
    private readonly EnrollmentVerifier _verifier;
    public RegistryCoordinator Registry { get; }
    public SignedRegistrationHandler Handler { get; }
    public ServingPlanState Plans { get; }

    private RegistrationRuntime(SqliteRegistryRepository repository, LocalSiteCertificateAuthority authority, RegistryCoordinator registry,
        EnrollmentVerifier verifier, SignedRegistrationHandler handler, ServingPlanState plans)
    {
        _repository = repository;
        _authority = authority;
        Registry = registry;
        _verifier = verifier;
        Handler = handler;
        Plans = plans;
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
        try
        {
            authority = LocalSiteCertificateAuthority.Open(controller.CertificateAuthorityPath, controller.EnrollmentRootFingerprint, clock);
            registry = new RegistryCoordinator(repository, availability, clock);
            await registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            using var root = authority.PublicCertificate;
            verifier = new EnrollmentVerifier(root, registry, clock);
            var handler = new SignedRegistrationHandler(bootstrap.SiteId, registry, availability, verifier, new EnrollmentChallenges(clock), clock);
            var runtime = new RegistrationRuntime(repository, authority, registry, verifier, handler, new ServingPlanState(bootstrap, authority));
            repository = null;
            authority = null;
            registry = null;
            verifier = null;
            return runtime;
        }
        finally
        {
            verifier?.Dispose();
            registry?.Dispose();
            authority?.Dispose();
            if (repository is not null) await repository.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _verifier.Dispose();
        Registry.Dispose();
        _authority.Dispose();
        await _repository.DisposeAsync().ConfigureAwait(false);
    }
}
