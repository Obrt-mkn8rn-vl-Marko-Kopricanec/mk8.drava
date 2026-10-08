using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Mk8.Drava.UnitTests;

internal sealed class DevelopmentDnsProvider : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<ProviderRequest> _requests = [];
    public List<ProviderRecord> Records { get; }
    public DevelopmentDnsProvider(List<ProviderRecord>? records = null) => Records = records ?? [];
    public Action? BeforeCreate { get; set; }
    public string Failure { get; set; } = "";
    public bool Block { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource FourEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ProviderRequest[] Requests { get { lock (_gate) return _requests.ToArray(); } }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Provider request URI is absent.");
        Assert.Equal("https://api.cloudflare.com", uri.GetLeftPart(UriPartial.Authority));
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(new string('A', 40), request.Headers.Authorization?.Parameter);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _requests.Add(new ProviderRequest(request.Method, uri, body));
            if (_requests.Count == 4) FourEntered.TrySetResult();
        }
        Entered.TrySetResult();
        if (Block)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
            finally { Exited.TrySetResult(); }
        }
        if (uri.AbsolutePath.EndsWith("/" + new string('a', 32), StringComparison.Ordinal))
            return Json(new { success = true, errors = Array.Empty<object>(), result = new { name = string.Equals(Failure, "zone", StringComparison.Ordinal) ? "foreign.example" : "site.example" } });
        if (request.Method == HttpMethod.Post) return CreateRecord(body);
        if (uri.AbsolutePath.Contains("/dns_records/", StringComparison.Ordinal)) return RecordById(request.Method, uri);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("name.exact=", uri.Query, StringComparison.Ordinal);
        if (string.Equals(Failure, "redirect", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://foreign.example/steal") } };
        if (string.Equals(Failure, "rate", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (string.Equals(Failure, "duplicate", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"success\":false,\"errors\":[],\"result\":[]}", Encoding.UTF8, "application/json") };
        if (Failure.StartsWith("oversize", StringComparison.Ordinal))
        {
            HttpContent content = string.Equals(Failure, "oversize-stream", StringComparison.Ordinal)
                ? new UnknownLengthProviderContent()
                : new ByteArrayContent(new byte[256 * 1024 + 1]);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
        var name = Uri.UnescapeDataString(uri.Query.Split('&')[0]["?name.exact=".Length..]);
        var ns = uri.Query.EndsWith("&type=NS", StringComparison.Ordinal);
        object[] found;
        lock (_gate) found = Records.Where(record => string.Equals(record.Name, name, StringComparison.Ordinal) && (!ns || string.Equals(record.Type, "NS", StringComparison.Ordinal))).Select(Record).ToArray();
        if (string.Equals(Failure, "malformed", StringComparison.Ordinal)) return Json(new { success = true, errors = Array.Empty<object>(), result = "not-an-array", result_info = new { page = 1, total_count = 0, total_pages = 0 } });
        return Json(new { success = !string.Equals(Failure, "rejected", StringComparison.Ordinal), errors = Array.Empty<object>(), result = found,
            result_info = new { page = 1, total_count = found.Length, total_pages = string.Equals(Failure, "pagination", StringComparison.Ordinal) ? 2 : 1 } });
    }

    private HttpResponseMessage CreateRecord(string body)
    {
        BeforeCreate?.Invoke();
        using var value = JsonDocument.Parse(body);
        var root = value.RootElement;
        var record = new ProviderRecord(root.GetProperty("name").GetString()!, root.GetProperty("type").GetString()!, root.GetProperty("content").GetString()!, false, root.GetProperty("comment").GetString()!) { Id = Guid.NewGuid().ToString("N") };
        lock (_gate) Records.Add(record);
        if (string.Equals(Failure, "create-response-lost", StringComparison.Ordinal)) throw new HttpRequestException("Development provider lost the create response after mutation.");
        return Json(new { success = true, errors = Array.Empty<object>(), result = Record(string.Equals(Failure, "created-host", StringComparison.Ordinal) ? record with { Name = "foreign.example" } : record) });
    }

    private HttpResponseMessage RecordById(HttpMethod method, Uri uri)
    {
        var id = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        ProviderRecord? record;
        lock (_gate) record = Records.Find(value => string.Equals(value.Id, id, StringComparison.Ordinal));
        if (record is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (method == HttpMethod.Delete)
        {
            lock (_gate) Records.Remove(record);
            if (string.Equals(Failure, "delete-response-lost", StringComparison.Ordinal)) throw new HttpRequestException("Development provider lost the delete response after mutation.");
            return Json(new { success = true, errors = Array.Empty<object>(), result = new { id = string.Equals(Failure, "delete-id", StringComparison.Ordinal) ? new string('c', 32) : id } });
        }
        Assert.Equal(HttpMethod.Get, method);
        record = Failure switch
        {
            "read-owner" => record with { Comment = "foreign owner" },
            "read-host" => record with { Name = "foreign.example" },
            "read-value" => record with { Content = "foreign value" },
            "read-type" => record with { Type = "A" },
            "read-id" => record with { Id = new string('c', 32) },
            "read-proxied" => record with { Proxied = true },
            _ => record,
        };
        return Json(new { success = true, errors = Array.Empty<object>(), result = Record(record) });
    }

    private static object Record(ProviderRecord record) => new { id = record.Id, name = record.Name, type = record.Type, content = record.Content, proxied = record.Proxied, comment = record.Comment };
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
