using System.Net.Http.Json;
using System.Text.Json;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.INF.NoConf;

internal sealed partial class CloudflareDnsApi
{
    public async ValueTask<CloudflareTxtRecord> CreateTxtAsync(string host, string value, int ttl, string ownership, CancellationToken cancellationToken)
    {
        RequireChallenge(host, value, ownership);
        if (ttl is < 60 or > 86400) throw new InvalidDataException("Challenge TTL exceeds its approved bounds.");
        using var content = JsonContent.Create(new { name = host, type = "TXT", content = "\"" + value + "\"", ttl, proxied = false, comment = ownership });
        using var response = await SendAsync(HttpMethod.Post, _zonePath + "/dns_records", content, cancellationToken).ConfigureAwait(false);
        var record = response.RootElement.GetProperty("result");
        var id = RequiredString(record, "id");
        RequireRecordId(id);
        var identity = new CloudflareTxtRecord(_zonePath, id, host, value, ownership);
        if (!MatchesTxt(record, identity)) throw new InvalidDataException("DNS provider did not confirm the owned challenge record.");
        return identity;
    }

    public async ValueTask<bool> DeleteTxtAsync(CloudflareTxtRecord identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RequireChallenge(identity.Host, identity.Value, identity.Ownership);
        RequireRecordId(identity.Id);
        if (!string.Equals(identity.ZonePath, _zonePath, StringComparison.Ordinal)) throw new InvalidDataException("Challenge belongs to another DNS zone.");
        var path = _zonePath + "/dns_records/" + identity.Id;
        using (var current = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false))
            if (!MatchesTxt(current.RootElement.GetProperty("result"), identity)) return false;
        using var response = await SendAsync(HttpMethod.Delete, path, null, cancellationToken).ConfigureAwait(false);
        return string.Equals(RequiredString(response.RootElement.GetProperty("result"), "id"), identity.Id, StringComparison.Ordinal);
    }

    public async ValueTask<CloudflareTxtRecord?> FindRecoveryTxtAsync(string host, string value, string ownership, string recordId, CancellationToken cancellationToken)
    {
        RequireChallenge(host, value, ownership);
        CloudflareTxtRecord? identity = null;
        if (recordId.Length != 0)
        {
            RequireRecordId(recordId);
            identity = new CloudflareTxtRecord(_zonePath, recordId, host, value, ownership);
            using var current = await SendCoreAsync(HttpMethod.Get, _zonePath + "/dns_records/" + recordId, null, allowNotFound: true, cancellationToken).ConfigureAwait(false);
            if (current is null) return null;
            if (!MatchesTxt(current.RootElement.GetProperty("result"), identity)) throw new InvalidDataException("Recovered DNS01 record no longer matches its durable ownership.");
        }
        else
        {
            using var records = await ListAsync(host, cancellationToken).ConfigureAwait(false);
            foreach (var record in records.RootElement.GetProperty("result").EnumerateArray())
            {
                if (!record.TryGetProperty("comment", out var comment) || comment.ValueKind != JsonValueKind.String || !string.Equals(comment.GetString(), ownership, StringComparison.Ordinal)) continue;
                var id = RequiredString(record, "id"); RequireRecordId(id);
                var found = new CloudflareTxtRecord(_zonePath, id, host, value, ownership);
                if (identity is not null || !MatchesTxt(record, found)) throw new InvalidDataException("Unconfirmed DNS01 creation has conflicting provider ownership.");
                identity = found;
            }
        }
        return identity;
    }

    private static bool MatchesTxt(JsonElement record, CloudflareTxtRecord identity) =>
        string.Equals(RequiredString(record, "id"), identity.Id, StringComparison.Ordinal) &&
        string.Equals(RequiredString(record, "name"), identity.Host, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(RequiredString(record, "type"), "TXT", StringComparison.Ordinal) &&
        string.Equals(RequiredString(record, "content"), "\"" + identity.Value + "\"", StringComparison.Ordinal) &&
        string.Equals(RequiredString(record, "comment"), identity.Ownership, StringComparison.Ordinal) &&
        record.GetProperty("proxied").ValueKind == JsonValueKind.False;

    private static void RequireRecordId(string id)
    {
        if (id.Length != 32) throw new InvalidDataException("DNS record identity is invalid.");
        foreach (var character in id)
            if (character is not (>= 'a' and <= 'f') and not (>= '0' and <= '9')) throw new InvalidDataException("DNS record identity is not canonical.");
    }

    private static void RequireChallenge(string host, string value, string ownership)
    {
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(ownership);
        const string prefix = "_acme-challenge.";
        if (!host.StartsWith(prefix, StringComparison.Ordinal) || host.Length is < 19 or > 253 || !host[prefix.Length..].Contains('.', StringComparison.Ordinal))
            throw new InvalidDataException("DNS challenge requires a canonical challenge owner name.");
        foreach (var label in host[prefix.Length..].Split('.')) RegistrationSiteTrust.RequireLabel(label);
        if (value.Length != 43) throw new InvalidDataException("DNS challenge requires its SHA-256 base64url digest.");
        foreach (var character in value)
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') throw new InvalidDataException("DNS challenge digest is not canonical.");
        if (!ownership.StartsWith("mk8.drava acme ", StringComparison.Ordinal) || ownership.Length is < 16 or > 256)
            throw new InvalidDataException("DNS challenge requires a bounded operation ownership marker.");
        foreach (var character in ownership)
            if (char.IsControl(character)) throw new InvalidDataException("DNS challenge ownership marker contains control characters.");
    }
}
