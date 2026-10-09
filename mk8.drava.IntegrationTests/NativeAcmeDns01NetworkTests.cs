using System.Text;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.INF.Dns.Management;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class NativeAcmeDns01NetworkTests
{
    private const string Owner = "_acme-challenge.site.test";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ005", Justification = "xUnit Assert.Single verifies exactly one item; LINQ First would remove the cardinality assertion.")]
    private static T Only<T>(IReadOnlyList<T> items) => Assert.Single(items);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupRemovesOnlyItsValueOrRefusesAnExternalRevisionGapAsync(bool externalEdit)
    {
        var (digest, ours, foreign) = CreateChallengeValues();
        var authority = new DevelopmentNativeDnsAuthority { Serial = 9 };
        await using var authorityLifetime = authority.ConfigureAwait(true);
        authority.Set(Owner, 16, foreign);
        var zone = new ChallengeZone(authority, ours, foreign);
        var management = await DevelopmentNativeDnsManagement.StartAsync(zone.HandleAsync, [new NativeDnsOwnerScope(Owner, 16)]).ConfigureAwait(true);
        await using var managementLifetime = management.ConfigureAwait(true);
        zone.Settings = management.Settings;
        var directory = Directory.CreateTempSubdirectory("dns_state_").FullName;
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var repository = await SqliteRegistryRepository.OpenAsync(directory, "site", CancellationToken.None).ConfigureAwait(true);
            await using var repositoryLifetime = repository.ConfigureAwait(true);
            using var provider = new NativeAcmeDns01ChallengeProvider(management.Settings, "site.test", "site", 300, "127.0.0.1", authority.Port, repository);
            var record = await provider.PublishAsync(Owner, digest, Guid.NewGuid().ToString("N"), CancellationToken.None).ConfigureAwait(true);
            Assert.True(await provider.IsPropagatedAsync(record, CancellationToken.None).ConfigureAwait(true));
            if (externalEdit)
            {
                zone.AdvanceExternalRevision();
                await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.RemoveAsync(record, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.RecoverAsync(CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
                Assert.Equal(0, zone.Removals);
                Assert.Contains(zone.Values, value => value.Span.SequenceEqual(ours));
            }
            else
            {
                await provider.RemoveAsync(record, CancellationToken.None).ConfigureAwait(true);
                await provider.RecoverAsync(CancellationToken.None).ConfigureAwait(true);
                Assert.Equal(1, zone.Removals);
                Assert.DoesNotContain(zone.Values, value => value.Span.SequenceEqual(ours));
            }
            Assert.Equal(1, zone.Additions);
            Assert.Contains(zone.Values, value => value.Span.SequenceEqual(foreign));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (string Digest, byte[] Ours, byte[] Foreign) CreateChallengeValues()
    {
        var digest = Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ours = new byte[] { 43 }.Concat(Encoding.ASCII.GetBytes(digest)).ToArray();
        var foreign = new byte[] { 7 }.Concat(Encoding.ASCII.GetBytes("foreign")).ToArray();
        return (digest, ours, foreign);
    }

    // Scripted controller state is separate from the caller-owned management and authority lifetimes.
    private sealed class ChallengeZone
    {
        private readonly DevelopmentNativeDnsAuthority authority;
        private readonly byte[] ours;
        private long revision = 9;

        public ChallengeZone(DevelopmentNativeDnsAuthority authority, byte[] ours, byte[] foreign)
        {
            this.authority = authority;
            this.ours = ours;
            Values = [foreign];
        }

        public List<ReadOnlyMemory<byte>> Values { get; }
        public NativeDnsManagementSettings? Settings { get; set; }
        public int Additions { get; private set; }
        public int Removals { get; private set; }

        public void AdvanceExternalRevision()
        {
            revision++;
            authority.Serial = checked((uint)revision);
        }

        public async Task HandleAsync(DevelopmentNativeDnsRequest request, HttpContext context)
        {
            if (string.Equals(request.Action, "read", StringComparison.Ordinal))
            {
                await ReplyToReadAsync(request, context).ConfigureAwait(false);
                return;
            }
            Assert.Equal("patch", request.Action);
            Assert.Equal(revision, request.Body!.ExpectedRevision);
            var change = Only(request.Body.Changes);
            Assert.False(change.Replace);
            Assert.Equal((ushort)16, change.Type);
            ApplyChange(change);
            revision++;
            authority.Serial = checked((uint)revision);
            authority.Set(Owner, 16, Values.ToArray());
            var settings = Settings ?? throw new InvalidOperationException("Synthetic management identity must precede client requests.");
            var location = ManagementHttpProtocol.OperationPath(settings.TenantId, settings.ZoneId, request.Operation, NativeDnsSession.WireName("site.test"));
            await DevelopmentNativeDnsManagement.ReplyAsync(context, new ManagementReply(request.Operation, revision, checked((uint)revision), new string('a', 64), "activated"), location).ConfigureAwait(false);
        }

        private async Task ReplyToReadAsync(DevelopmentNativeDnsRequest request, HttpContext context)
        {
            var reply = new ManagementReply(request.Operation, revision, checked((uint)revision), new string('a', 64), "current")
            {
                Records = Values.Select(value => new ZoneRecordData(NativeDnsSession.WireName(Owner), 16, 300, value)).ToArray(),
            };
            await DevelopmentNativeDnsManagement.ReplyAsync(context, reply).ConfigureAwait(false);
        }

        private void ApplyChange(RrsetChange change)
        {
            if (change.Add.Count == 1)
            {
                Assert.Empty(change.Remove);
                Assert.Equal(ours, change.Add[0].ToArray());
                Values.Add(change.Add[0]);
                Additions++;
            }
            else
            {
                Assert.Empty(change.Add);
                Assert.Equal(ours, Only(change.Remove).ToArray());
                Values.RemoveAll(value => value.Span.SequenceEqual(change.Remove[0].Span));
                Removals++;
            }
        }
    }
}
