using System.Net;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.INF.Dns.Management;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class NativeDnsManagementNetworkTests
{
    private static readonly byte[] Value = [192, 0, 2, 7];
    private static readonly string Hash = new('a', 64);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "xUnit Assert.Single verifies exactly one item; LINQ First would remove the cardinality assertion.")]
    private static T Only<T>(IReadOnlyList<T> items) => Assert.Single(items);

    [Fact]
    public async Task LostMutationReplyPersistsIdentityAndRestartUsesStatusWithoutReplayAsync()
    {
        NativeDnsSession? session = null;
        var patches = 0; var statuses = 0; Guid observed = Guid.Empty;
        var server = await DevelopmentNativeDnsManagement.StartAsync(async (request, context) =>
        {
            if (string.Equals(request.Action, "patch", StringComparison.Ordinal))
            {
                patches++; observed = request.Operation;
                await AssertPreparedMutationAsync(session!, request, context.RequestAborted).ConfigureAwait(false);
                context.Abort(); return;
            }
            Assert.Equal("status", request.Action); statuses++; Assert.Equal(observed, request.Operation);
            await DevelopmentNativeDnsManagement.ReplyAsync(context, new ManagementReply(request.Operation, 10, 17, Hash, "activated")).ConfigureAwait(false);
        }).ConfigureAwait(true);
        await using var serverLifetime = server.ConfigureAwait(true);
        var state = Directory.CreateTempSubdirectory("dns_state_").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var repository = await SqliteRegistryRepository.OpenAsync(state, "site", CancellationToken.None).ConfigureAwait(true);
            await using (repository.ConfigureAwait(true))
            {
                session = new NativeDnsSession(server.Settings, "site.test", "site", false, repository, ["192.0.2.7"]);
                using (session)
                {
                    var entry = session.Intent("svc.site.test", 1, 300, Value, 9, Guid.NewGuid());
                    await Assert.ThrowsAsync<HttpRequestException>(async () => await session.PrepareAndSendAsync(entry, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
                    Assert.Equal("prepared", Only((await session.ReadJournalAsync(CancellationToken.None).ConfigureAwait(true)).Entries).State);
                    Assert.Equal(1, patches);
                }
            }
            await AssertRecoveryReceiptAsync(state, server.Settings, observed).ConfigureAwait(true);
            Assert.Equal(1, patches); Assert.Equal(1, statuses);
        }
        finally { Directory.Delete(state, recursive: true); }
    }

    private static async Task AssertPreparedMutationAsync(NativeDnsSession session, DevelopmentNativeDnsRequest request, CancellationToken cancellationToken)
    {
        var prepared = Only((await session.ReadJournalAsync(cancellationToken).ConfigureAwait(false)).Entries);
        Assert.Equal("prepared", prepared.State);
        Assert.Equal(request.Operation, prepared.OperationId);
        var body = request.Body!;
        var change = Only(body.Changes);
        Assert.False(change.Replace);
        Assert.Empty(change.Remove);
        Assert.Equal(Value, Only(change.Add).ToArray());
        Assert.Equal(9, body.ExpectedRevision);
        Assert.Empty(body.Records);
        Assert.Empty(body.Selection);
    }

    private static async Task AssertRecoveryReceiptAsync(string state, Mk8.Drava.Configuration.NativeDnsManagementSettings settings, Guid observed)
    {
        var reopened = await SqliteRegistryRepository.OpenAsync(state, "site", CancellationToken.None).ConfigureAwait(true);
        await using var reopenedLifetime = reopened.ConfigureAwait(true);
        using var restarted = new NativeDnsSession(settings, "site.test", "site", false, reopened, ["192.0.2.7"]);
        var retained = Only((await restarted.ReadJournalAsync(CancellationToken.None).ConfigureAwait(true)).Entries);
        var receipt = await restarted.RecoverAsync(retained, CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(receipt); Assert.Equal("activated", receipt.State); Assert.Equal(observed, receipt.OperationId);
        Assert.Equal(10, receipt.Revision); Assert.Equal(17u, receipt.Serial); Assert.Equal(Hash, receipt.ContentHash);
        Assert.Contains(observed.ToString("D"), receipt.OperationLocation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbsentStatusOrInvalidLocationRetainsPreparedIntentWithoutAnotherPatchAsync(bool invalidLocation)
    {
        var patches = 0; var statuses = 0;
        var server = await DevelopmentNativeDnsManagement.StartAsync(async (request, context) =>
        {
            context.Response.Headers[ManagementHttpProtocol.VersionHeader] = ManagementHttpProtocol.Version;
            if (string.Equals(request.Action, "patch", StringComparison.Ordinal))
            {
                patches++;
                if (!invalidLocation) { context.Abort(); return; }
                await DevelopmentNativeDnsManagement.ReplyAsync(context, new ManagementReply(request.Operation, 10, 17, Hash, "activated"), "/v2/foreign-operation").ConfigureAwait(false);
                return;
            }
            statuses++; context.Response.StatusCode = 404;
        }).ConfigureAwait(true);
        await using var serverLifetime = server.ConfigureAwait(true);
        var state = Directory.CreateTempSubdirectory("dns_state_").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var repository = await SqliteRegistryRepository.OpenAsync(state, "site", CancellationToken.None).ConfigureAwait(true);
            await using var repositoryLifetime = repository.ConfigureAwait(true);
            using var session = new NativeDnsSession(server.Settings, "site.test", "site", false, repository, ["192.0.2.7"]);
            var entry = session.Intent("svc.site.test", 1, 300, Value, 9, Guid.NewGuid());
            if (invalidLocation)
                await Assert.ThrowsAsync<InvalidDataException>(async () => await session.PrepareAndSendAsync(entry, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
            else
                await Assert.ThrowsAsync<HttpRequestException>(async () => await session.PrepareAndSendAsync(entry, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
            Assert.Null(await session.RecoverAsync(entry, CancellationToken.None).ConfigureAwait(true));
            Assert.Equal("prepared", Only((await session.ReadJournalAsync(CancellationToken.None).ConfigureAwait(true)).Entries).State);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await session.PrepareAndSendAsync(entry, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
            Assert.Equal(1, patches); Assert.Equal(1, statuses);
        }
        finally { Directory.Delete(state, recursive: true); }
    }

    [Fact]
    public async Task AcceptedReceiptNeedsStatusAndHistoricalActivationNeverResendsMutationAsync()
    {
        var patches = 0; var statuses = 0;
        DevelopmentNativeDnsManagement? server = null;
        server = await DevelopmentNativeDnsManagement.StartAsync(async (request, context) =>
        {
            var state = string.Equals(request.Action, "patch", StringComparison.Ordinal) ? "accepted" : "activated";
            if (string.Equals(request.Action, "patch", StringComparison.Ordinal)) patches++; else statuses++;
            var location = string.Equals(request.Action, "patch", StringComparison.Ordinal) ? ManagementHttpProtocol.OperationPath(server!.Settings.TenantId, server.Settings.ZoneId, request.Operation, NativeDnsSession.WireName("site.test")) : null;
            await DevelopmentNativeDnsManagement.ReplyAsync(context, new ManagementReply(request.Operation, 10, 17, Hash, state), location).ConfigureAwait(false);
        }).ConfigureAwait(true);
        await using var serverLifetime = server.ConfigureAwait(true);
        var directory = Directory.CreateTempSubdirectory("dns_state_").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var repository = await SqliteRegistryRepository.OpenAsync(directory, "site", CancellationToken.None).ConfigureAwait(true);
            await using var repositoryLifetime = repository.ConfigureAwait(true);
            using var session = new NativeDnsSession(server.Settings, "site.test", "site", false, repository, ["192.0.2.7"]);
            var entry = await session.PrepareAndSendAsync(session.Intent("svc.site.test", 1, 300, Value, 9, Guid.NewGuid()), CancellationToken.None).ConfigureAwait(true);
            Assert.Equal("accepted", entry.State);
            var activated = await session.RecoverAsync(entry, CancellationToken.None).ConfigureAwait(true);
            Assert.NotNull(activated); Assert.Equal("activated", activated.State);
            Assert.Equal(activated, await session.RecoverAsync(activated, CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(1, patches); Assert.Equal(1, statuses);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
