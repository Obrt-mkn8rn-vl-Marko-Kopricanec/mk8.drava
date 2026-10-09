using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.INF.Dns.Management;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class NativeDnsPublicationNetworkTests
{
    private static readonly byte[] Address = [192, 0, 2, 7];
    private static readonly string Hash = new('a', 64);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "xUnit Assert.Single verifies exactly one item; LINQ First would remove the cardinality assertion.")]
    private static T Only<T>(IReadOnlyList<T> items) => Assert.Single(items);

    [Fact]
    public async Task AcceptedAndActivatedReceiptsCannotAdmitUntilAuthoritativeSerialAndValueConvergeAsync()
    {
        var authority = new DevelopmentNativeDnsAuthority { Serial = 9 };
        await using var authorityLifetime = authority.ConfigureAwait(true);
        var present = false; var patches = 0; Guid operation = Guid.Empty;
        DevelopmentNativeDnsManagement? management = null;
        management = await DevelopmentNativeDnsManagement.StartAsync(async (request, context) =>
        {
            if (string.Equals(request.Action, "read", StringComparison.Ordinal))
            {
                var records = present ? new[] { new ZoneRecordData(NativeDnsSession.WireName("svc.site.test"), 1, 300, Address) } : [];
                await DevelopmentNativeDnsManagement.ReplyAsync(context, new ManagementReply(request.Operation, present ? 10 : 9, present ? 10u : 9u, Hash, "current") { Records = records }).ConfigureAwait(false);
            }
            else if (string.Equals(request.Action, "patch", StringComparison.Ordinal))
            {
                patches++; operation = request.Operation; present = true;
                Assert.Equal(9, request.Body!.ExpectedRevision);
                var change = Only(request.Body.Changes);
                Assert.False(change.Replace); Assert.Empty(change.Remove);
                var location = ManagementHttpProtocol.OperationPath(management!.Settings.TenantId, management.Settings.ZoneId, request.Operation, NativeDnsSession.WireName("site.test"));
                await DevelopmentNativeDnsManagement.ReplyAsync(context, new ManagementReply(request.Operation, 10, 10, Hash, "accepted"), location).ConfigureAwait(false);
            }
            else
            {
                Assert.Equal(operation, request.Operation);
                await DevelopmentNativeDnsManagement.ReplyAsync(context, new ManagementReply(request.Operation, 10, 10, Hash, "activated")).ConfigureAwait(false);
            }
        }).ConfigureAwait(true);
        await using var managementLifetime = management.ConfigureAwait(true);
        var directory = Directory.CreateTempSubdirectory("dns_state_").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var repository = await SqliteRegistryRepository.OpenAsync(directory, "site", CancellationToken.None).ConfigureAwait(true);
            await using var repositoryLifetime = repository.ConfigureAwait(true);
            using var verifier = new NativeServiceDnsVerifier(new DnsPublicationSettings { Provider = "mk8.dns", NativeManagement = management.Settings, RequestTimeoutSeconds = 20 },
                "site.test", "site", ["192.0.2.7"], "127.0.0.1", authority.Port, repository);
            Assert.False((await verifier.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
            Assert.False((await verifier.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
            authority.Serial = 10; authority.Set("svc.site.test", 1, Address);
            Assert.True((await verifier.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
            await AssertAuthorityNegativesAsync(verifier, authority).ConfigureAwait(true);
            Assert.Equal(1, patches);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task AssertAuthorityNegativesAsync(NativeServiceDnsVerifier verifier, DevelopmentNativeDnsAuthority authority)
    {
        authority.Set("svc.site.test", 1, new byte[] { 192, 0, 2, 99 });
        Assert.False((await verifier.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
        authority.Set("svc.site.test", 1, Address); authority.Authoritative = false;
        Assert.False((await verifier.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
        Assert.False((await verifier.VerifyAsync("unknown.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
    }

    [Fact]
    public async Task DirectObservationUsesTcpFallbackAndPreservesForeignTxtOnAbsenceProofAsync()
    {
        var authority = new DevelopmentNativeDnsAuthority { Truncate = true, Serial = 17 };
        await using var lifetime = authority.ConfigureAwait(true);
        var verifier = new NativeDnsServingVerifier("127.0.0.1", authority.Port, "site.test");
        var ours = new byte[] { 3, (byte)'o', (byte)'u', (byte)'r' };
        var foreign = new byte[] { 3, (byte)'d', (byte)'n', (byte)'s' };
        authority.Set("_acme-challenge.site.test", 16, ours, foreign);
        Assert.True(await verifier.VerifyAsync("_acme-challenge.site.test", 16, [ours], 17, CancellationToken.None).ConfigureAwait(true));
        Assert.False(await verifier.VerifyAbsentAsync("_acme-challenge.site.test", 16, ours, 17, CancellationToken.None).ConfigureAwait(true));
        authority.Set("_acme-challenge.site.test", 16, foreign);
        Assert.True(await verifier.VerifyAbsentAsync("_acme-challenge.site.test", 16, ours, 17, CancellationToken.None).ConfigureAwait(true));
        authority.Set("_acme-challenge.site.test", 16);
        Assert.True(await verifier.VerifyAbsentAsync("_acme-challenge.site.test", 16, ours, 17, CancellationToken.None).ConfigureAwait(true));
        Assert.True(authority.TcpQueries >= 10);
    }
}
