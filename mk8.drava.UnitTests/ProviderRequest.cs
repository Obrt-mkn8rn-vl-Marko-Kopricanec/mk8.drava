using System.Net.Http;

namespace Mk8.Drava.UnitTests;

internal sealed record ProviderRequest(HttpMethod Method, Uri Uri, string Body);
