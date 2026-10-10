using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mk8.Drava.Application.INF.NoConf;

internal sealed partial class CloudflareDnsApi : IDisposable
{
    private const int MaximumResponseBytes = 256 * 1024;
    private readonly HttpClient _client;
    private readonly HttpMessageHandler _handler;
    private readonly string _zonePath;
    private readonly string _credential;

    public CloudflareDnsApi(Uri apiBaseUrl, string zoneId, string credential, HttpMessageHandler? handler)
    {
        _zonePath = "zones/" + zoneId;
        _credential = credential;
        _handler = handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 4 };
        try
        {
            _client = new HttpClient(_handler, disposeHandler: false);
            _client.BaseAddress = apiBaseUrl;
            _client.Timeout = Timeout.InfiniteTimeSpan;
        }
        catch { _client?.Dispose(); _handler.Dispose(); throw; }
    }

    public async ValueTask<string> ReadZoneNameAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, _zonePath, null, cancellationToken).ConfigureAwait(false);
        return RequiredString(response.RootElement.GetProperty("result"), "name");
    }

    public async ValueTask<JsonDocument> ListAsync(string host, CancellationToken cancellationToken, bool nameServersOnly = false)
    {
        var response = await SendAsync(HttpMethod.Get, _zonePath + "/dns_records?name.exact=" + Uri.EscapeDataString(host) + "&page=1&per_page=128" + (nameServersOnly ? "&type=NS" : ""), null, cancellationToken).ConfigureAwait(false);
        try
        {
            var root = response.RootElement;
            var records = root.GetProperty("result");
            var paging = root.GetProperty("result_info");
            if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() > 128 ||
                paging.GetProperty("page").GetInt32() != 1 || paging.GetProperty("total_pages").GetInt32() is < 0 or > 1 ||
                paging.GetProperty("total_count").GetInt32() != records.GetArrayLength())
                throw new InvalidDataException("DNS provider returned an incomplete record set.");
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    public async ValueTask<bool> CreateAsync(string host, string type, string address, int ttl, string ownership, CancellationToken cancellationToken)
    {
        if (string.Equals(type, "TXT", StringComparison.Ordinal))
        {
            await CreateTxtAsync(host, address, ttl, ownership, cancellationToken).ConfigureAwait(false);
            return true;
        }
        using var content = JsonContent.Create(new { name = host, type, content = address, ttl, proxied = false, comment = ownership });
        using var response = await SendAsync(HttpMethod.Post, _zonePath + "/dns_records", content, cancellationToken).ConfigureAwait(false);
        var record = response.RootElement.GetProperty("result");
        return string.Equals(RequiredString(record, "name"), host, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RequiredString(record, "type"), type, StringComparison.Ordinal) &&
            System.Net.IPAddress.TryParse(RequiredString(record, "content"), out var returnedAddress) && returnedAddress.Equals(System.Net.IPAddress.Parse(address)) &&
            record.GetProperty("proxied").ValueKind == JsonValueKind.False &&
            string.Equals(RequiredString(record, "comment"), ownership, StringComparison.Ordinal) &&
            RequiredString(record, "id").Length is > 0 and <= 64;
    }

    private async ValueTask<JsonDocument> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken) =>
        await SendCoreAsync(method, path, content, allowNotFound: false, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("DNS provider response is absent.");

    private async ValueTask<JsonDocument?> SendCoreAsync(HttpMethod method, string path, HttpContent? content, bool allowNotFound, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credential);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("DNS provider rejected the operation or exceeded its response bound.");
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        var bytes = new byte[MaximumResponseBytes + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumResponseBytes) throw new InvalidDataException("DNS provider exceeded its response bound.");
        var document = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 16 });
        try
        {
            RequireUniqueProperties(document.RootElement);
            if (document.RootElement.GetProperty("success").ValueKind != JsonValueKind.True ||
                document.RootElement.GetProperty("errors").ValueKind != JsonValueKind.Array || document.RootElement.GetProperty("errors").GetArrayLength() != 0)
                throw new InvalidDataException("DNS provider did not confirm the operation.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static string RequiredString(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidDataException("DNS provider returned an absent field.");

    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("DNS provider returned a duplicate property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RequireUniqueProperties(item);
    }

    public void Dispose() { _client.Dispose(); _handler.Dispose(); }
}
