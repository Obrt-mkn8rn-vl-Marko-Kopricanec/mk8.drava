using System.Text.Json;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.INF.Acme;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentJournaledAcmeProvider : IDisposable
{
    public DevelopmentDnsProvider Handler { get; }
    public CloudflareAcmeDns01ChallengeProvider Provider { get; }
    public AcmeDns01CleanupJournal Journal { get; }

    public DevelopmentJournaledAcmeProvider(string directory, List<ProviderRecord> records)
    {
        var settings = new DnsPublicationSettings { Provider = "cloudflare", ApiBaseUrl = new Uri("https://api.dns.invalid/client/v4/"), ZoneId = new string('a', 32), ZoneName = "site.example", Credential = new SecretText(new string('A', 40)) };
        Journal = new AcmeDns01CleanupJournal(Path.Combine(directory, "cleanup.json"));
        Handler = new DevelopmentDnsProvider(records);
        try { Provider = new CloudflareAcmeDns01ChallengeProvider(settings, "site.example", "development", new Verifier(), Handler, Journal); }
        catch { Handler.Dispose(); Journal.Dispose(); throw; }
    }

    public static void AssertIntentPersisted(string directory, string operation)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "cleanup.json")));
        var entries = document.RootElement.GetProperty("entries"); Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal(operation, entries[0].GetProperty("OperationId").GetString()); Assert.Equal("", entries[0].GetProperty("RecordId").GetString());
    }

    public void Dispose() { Provider.Dispose(); Handler.Dispose(); Journal.Dispose(); }

    private sealed class Verifier : IAcmeDns01PropagationVerifier
    {
        public ValueTask<bool> VerifyAsync(string host, string value, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(true); }
    }
}
