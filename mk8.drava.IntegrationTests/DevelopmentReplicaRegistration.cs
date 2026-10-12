using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.IntegrationTests;

internal static class DevelopmentReplicaRegistration
{
    public static RegistrationIdentity Identity() => new()
    {
        SiteId = "development", NodeId = "local", OwnerId = "development", ServiceId = "svc", ContractId = "v1",
        InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N"),
    };

    public static RegistrationCommand Register(RegistrationIdentity identity, int port) => new()
    {
        Identity = identity, Operation = RegistrationOperation.Register,
        Advertisement = new ServiceAdvertisement { DeploymentId = "development", Address = "127.0.0.1", Port = port, ReadinessPath = "/ready" },
    };

    public static async Task WaitReadyAsync(DevelopmentRegistrationClient client, RegistrationIdentity identity, CancellationToken cancellationToken)
    {
        while (true)
        {
            var status = await client.SubmitAsync(new RegistrationCommand { Identity = identity, Operation = RegistrationOperation.Status }, cancellationToken).ConfigureAwait(false);
            if (status.Phase == RegistrationPhase.Ready) return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}
