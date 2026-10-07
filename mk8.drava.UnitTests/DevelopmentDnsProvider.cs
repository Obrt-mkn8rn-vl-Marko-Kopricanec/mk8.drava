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
    public List<ProviderRecord> Records { get; } = [];
    public string Failure { get; set; } = "";
    public bool Block { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        if (uri.AbsolutePath.EndsWith("/" + new string('a', 32), StringComparison.Ordinal))
            return Json(new { success = true, errors = Array.Empty<object>(), result = new { name = string.Equals(Failure, "zone", StringComparison.Ordinal) ? "foreign.example" : "site.example" } });
        if (request.Method == HttpMethod.Post)
        {
            using var value = JsonDocument.Parse(body);
            var root = value.RootElement;
            var record = new ProviderRecord(root.GetProperty("name").GetString()!, root.GetProperty("type").GetString()!, root.GetProperty("content").GetString()!, false, root.GetProperty("comment").GetString()!);
            lock (_gate) Records.Add(record);
            return Json(new { success = true, errors = Array.Empty<object>(), result = Record(string.Equals(Failure, "created-host", StringComparison.Ordinal) ? record with { Name = "foreign.example" } : record) });
        }
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

    private static object Record(ProviderRecord record) => new { id = new string('b', 32), name = record.Name, type = record.Type, content = record.Content, proxied = record.Proxied, comment = record.Comment };
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
