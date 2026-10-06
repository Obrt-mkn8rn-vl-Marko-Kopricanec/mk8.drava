using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Contracts.Administration.V1;
using Mk8.Drava.Contracts.Registration.V1;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class PolicyAdministrationTests
{
    [Fact]
    public async Task AcceptedControlPolicySurvivesRestartConflictsAndBadFilesWithoutRestoringMembershipLeasesAsync()
    {
        var upstream = await DevelopmentHttpUpstream.StartAsync(context => context.Response.WriteAsync("policy-body")).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var dns = new DevelopmentDnsServer(IPAddress.Loopback);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, enrolledSite: true, administration: true, manualRoute: false, dnsPort: dns.Port).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var administrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        var identity = await RegistryAdministrationTests.RegisterAsync(proxy, upstream.Port).ConfigureAwait(true);
        var initial = await ReadAsync(administrator.Client).ConfigureAwait(true);
        using var policy = JsonDocument.Parse("{\"site\":{\"algorithm\":\"least-active\",\"headers\":{\"setResponseHeaders\":[{\"name\":\"X-Control\",\"value\":\"applied\"}]}}}");
        using var updated = await administrator.Client.PostAsJsonAsync(new Uri("/admin/drava/policy", UriKind.Relative), new PolicyUpdateRequest { ExpectedRevision = initial.AcceptedRevision, Policy = policy.RootElement }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var accepted = await updated.Content.ReadFromJsonAsync<PolicyStateResponse>().ConfigureAwait(true);
        Assert.NotNull(accepted);
        Assert.Equal(initial.AcceptedRevision + 1, accepted.AcceptedRevision);
        Assert.Equal(accepted.AcceptedRevision, accepted.AppliedRevision);
        Assert.Collection(accepted.Services, static service =>
        {
            Assert.Equal("LeastActive", service.Algorithm);
            Assert.Equal("site", service.Provenance["balancing"]);
            Assert.Equal("site", service.Provenance["headers"]);
        });
        await AssertRejectionsRetainPolicyAsync(administrator.Client, initial.AcceptedRevision, accepted.AcceptedRevision).ConfigureAwait(true);
        await proxy.WritePolicyAsync("{broken").ConfigureAwait(true);
        await RegistryAdministrationTests.WaitReadyAsync(proxy, identity).ConfigureAwait(true);
        using (var serving = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test"))
        using (var body = await serving.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true))
        {
            Assert.Equal(HttpStatusCode.OK, body.StatusCode);
            Assert.Equal("policy-body", await body.Content.ReadAsStringAsync().ConfigureAwait(true));
            Assert.Collection(body.Headers.GetValues("X-Control"), static value => Assert.Equal("applied", value));
        }
        await proxy.RestartAsync().ConfigureAwait(true);
        await AssertRestartAndRollbackAsync(proxy, initial.AcceptedRevision, accepted).ConfigureAwait(true);
    }

    private static async Task AssertRestartAndRollbackAsync(TwoProcessProxy proxy, long originalRevision, PolicyStateResponse accepted)
    {
        using var restartedAdministrator = await AdministrationProcessTests.CreateAdministratorAsync(proxy).ConfigureAwait(true);
        var restored = await ReadAsync(restartedAdministrator.Client).ConfigureAwait(true);
        Assert.Equal(accepted.AcceptedRevision, restored.AcceptedRevision);
        Assert.Equal("control", restored.Source);
        Assert.Equal(accepted.Digest, restored.Digest);
        using var registry = await restartedAdministrator.Client.GetAsync(new Uri("/admin/drava/registry", UriKind.Relative)).ConfigureAwait(true);
        var registryView = await registry.Content.ReadFromJsonAsync<RegistryStateResponse>().ConfigureAwait(true);
        Assert.NotNull(registryView);
        Assert.Collection(registryView.Instances, static instance => Assert.False(instance.LeaseValid));
        using var restartedServing = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var ineligible = await restartedServing.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ineligible.StatusCode);
        using var rolledBack = await restartedAdministrator.Client.PostAsJsonAsync(new Uri("/admin/drava/policy/rollback", UriKind.Relative), new PolicyRollbackRequest { ExpectedRevision = restored.AcceptedRevision, TargetRevision = originalRevision }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, rolledBack.StatusCode);
        var rollback = await rolledBack.Content.ReadFromJsonAsync<PolicyStateResponse>().ConfigureAwait(true);
        Assert.NotNull(rollback);
        Assert.Equal(restored.AcceptedRevision + 1, rollback.AcceptedRevision);
        Assert.Collection(rollback.Services, static service => Assert.Equal("PowerOfTwoChoices", service.Algorithm));
        await proxy.WritePolicyAsync("{\"site\":{\"algorithm\":\"weighted-round-robin\"}}").ConfigureAwait(true);
        using var imported = await restartedAdministrator.Client.PostAsJsonAsync(new Uri("/admin/drava/policy", UriKind.Relative), new PolicyUpdateRequest { ExpectedRevision = rollback.AcceptedRevision, ImportFile = true }).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var filePolicy = await imported.Content.ReadFromJsonAsync<PolicyStateResponse>().ConfigureAwait(true);
        Assert.NotNull(filePolicy);
        Assert.Equal("file", filePolicy.Source);
        Assert.Collection(filePolicy.Services, static service => Assert.Equal("WeightedRoundRobin", service.Algorithm));
    }

    private static async Task AssertRejectionsRetainPolicyAsync(HttpClient client, long staleRevision, long currentRevision)
    {
        using var stalePolicy = JsonDocument.Parse("{}");
        using var stale = await client.PostAsJsonAsync(new Uri("/admin/drava/policy", UriKind.Relative), new PolicyUpdateRequest { ExpectedRevision = staleRevision, Policy = stalePolicy.RootElement }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var invalidPolicy = JsonDocument.Parse("{\"site\":{\"affinityHeader\":\"X-Tenant\"}}");
        using var invalid = await client.PostAsJsonAsync(new Uri("/admin/drava/policy", UriKind.Relative), new PolicyUpdateRequest { ExpectedRevision = currentRevision, Policy = invalidPolicy.RootElement }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var retained = await ReadAsync(client).ConfigureAwait(false);
        Assert.Equal(currentRevision, retained.AcceptedRevision);
        Assert.Equal(2, retained.History.Count);
    }

    private static async Task<PolicyStateResponse> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/admin/drava/policy?includeHistory=true", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<PolicyStateResponse>().ConfigureAwait(false) ?? throw new InvalidDataException("Policy view is missing.");
    }
}
