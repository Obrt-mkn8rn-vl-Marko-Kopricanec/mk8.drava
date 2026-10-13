using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Relay;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class NodeAgentProfileLeaseProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctStateCannotReplaceALiveSharedEnrollmentProfileAndCanReopenAfterOwnerExitAsync(bool disableImplicitLocking)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = disableImplicitLocking ? "1" : "0",
        };
        var proxy = await TwoProcessProxy.StartAsync(1, enrolledSite: true, manualRoute: false, relayNode: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var owner = await DevelopmentNodeAgent.StartAsync(proxy, environment: environment).ConfigureAwait(true);
        await using var ownerLifetime = owner.ConfigureAwait(true);
        var profilePath = proxy.NodeAgentDescriptorPath;
        var originalProfile = await File.ReadAllBytesAsync(profilePath).ConfigureAwait(true);
        Assert.DoesNotContain(proxy.NodeAgentToken.Reveal(), System.Text.Encoding.UTF8.GetString(originalProfile), StringComparison.Ordinal);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            // If the implementation wrongly admits the contender, this scope still retires it.
            var contender = await DevelopmentNodeAgent.StartAsync(proxy, stateDirectoryName: "contender", environment: environment).ConfigureAwait(true);
            await using var contenderLifetime = contender.ConfigureAwait(true);
        }).ConfigureAwait(true);
        Assert.True(refusal.Message.Contains(".agent.lock", StringComparison.Ordinal) ||
            refusal.Message.Contains("Private writer lease is unavailable", StringComparison.Ordinal),
            "Contender must be refused at enrollment-profile lease acquisition.");
        Assert.Equal(originalProfile, await File.ReadAllBytesAsync(profilePath).ConfigureAwait(true));
        await AssertLocalOwnerAsync(owner, proxy.NodeAgentToken, proxy.EnrolledNodeId).ConfigureAwait(true);

        await owner.DisposeAsync().ConfigureAwait(true);
        var reopened = await DevelopmentNodeAgent.StartAsync(proxy, stateDirectoryName: "contender", environment: environment).ConfigureAwait(true);
        await using var reopenedLifetime = reopened.ConfigureAwait(true);
        Assert.NotEqual(owner.Descriptor.AgentBootId, reopened.Descriptor.AgentBootId, StringComparer.Ordinal);
        Assert.Equal(owner.Descriptor.CertificateFingerprint, reopened.Descriptor.CertificateFingerprint, StringComparer.Ordinal);
        await AssertLocalOwnerAsync(reopened, proxy.NodeAgentToken, proxy.EnrolledNodeId).ConfigureAwait(true);
    }

    private static async Task AssertLocalOwnerAsync(DevelopmentNodeAgent owner, SecretText token, string nodeId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new NodeAgentLocalClient(owner.Descriptor, token);
        // Draining an absent synthetic instance has no mapping side effect, but authenticates
        // the private socket and requires the response to belong to this exact live boot.
        var endpoint = await client.ApplyAsync(new RegistrationCommand
        {
            Operation = RegistrationOperation.Drain,
            Identity = new RegistrationIdentity
            {
                SiteId = "development", NodeId = nodeId, OwnerId = "development", ServiceId = "svc",
                ContractId = "http", InstanceId = Guid.NewGuid().ToString("N"), BootId = Guid.NewGuid().ToString("N"),
            },
        }, deadline.Token).ConfigureAwait(true);
        Assert.Equal(owner.Descriptor.AgentBootId, endpoint.AgentBootId, StringComparer.Ordinal);
        Assert.Equal(owner.Descriptor.CertificateFingerprint, endpoint.CertificateFingerprint, StringComparer.Ordinal);
        Assert.Equal(owner.Descriptor.RelayAddress, endpoint.Address, StringComparer.Ordinal);
        Assert.Equal(owner.Descriptor.RelayPort, endpoint.Port);
    }
}
