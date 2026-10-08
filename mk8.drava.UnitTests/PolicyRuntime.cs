using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Application.INF.Runtime;

namespace Mk8.Drava.UnitTests;

internal sealed class PolicyRuntime : IAsyncDisposable
{
    public PolicyRuntime(SqliteRegistryRepository repository, string directory, IPolicyRepository? policies = null)
    {
        Availability = new DestinationAvailabilityStore(TimeProvider.System);
        Registry = new RegistryCoordinator(repository, Availability, TimeProvider.System);
        var store = new ProxyConfigurationStore();
        store.Replace(NoConfCompilerTests.Baseline());
        var site = new UnpublishedSite();
        Reconciler = new NoConfReconciler(Registry, Availability, store, NoConfCompilerTests.Compiler(), site, site, site,
            Path.Combine(directory, "noconf.json"), "site.example", "node", TimeProvider.System, NullLogger<NoConfReconciler>.Instance, policies ?? repository);
    }

    public DestinationAvailabilityStore Availability { get; }
    public RegistryCoordinator Registry { get; }
    public NoConfReconciler Reconciler { get; }

    public async Task StartAsync()
    {
        await Registry.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        await Reconciler.StartAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<NoConfPolicyView> WaitAsync(Func<NoConfPolicyView, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var view = await Reconciler.ReadPolicyViewAsync(includeHistory: true, timeout.Token).ConfigureAwait(false);
            if (condition(view)) return view;
            await Task.Delay(25, timeout.Token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await Reconciler.StopAsync(timeout.Token).ConfigureAwait(false); }
        finally { Reconciler.Dispose(); Registry.Dispose(); }
    }

    private sealed class UnpublishedSite : IRegisteredReadinessProbe, IServiceDnsVerifier, IGatewayPublicationSource
    {
        public ValueTask<bool> CheckAsync(InstanceIntent intent, RuntimeUpstream upstream, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(intent);
            ArgumentNullException.ThrowIfNull(upstream);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }
        public ValueTask<DnsPublicationProof> VerifyAsync(string host, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(host);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(DnsPublicationProof.Missing);
        }
        public GatewayPublicationProof? ReadPublicationProof() => null;
    }
}
