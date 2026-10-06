using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHeaderPolicyProjection
{
    public RuntimeHeaderPolicyProjection(IEnumerable<RuntimeHeaderFieldProjection> SetRequestHeaders, IEnumerable<string> RemoveRequestHeaders, IEnumerable<RuntimeHeaderFieldProjection> SetResponseHeaders, IEnumerable<string> RemoveResponseHeaders)
    {
        this.SetRequestHeaders = CopySetHeaders(SetRequestHeaders);
        this.RemoveRequestHeaders = CopyRemoveHeaders(RemoveRequestHeaders);
        this.SetResponseHeaders = CopySetHeaders(SetResponseHeaders);
        this.RemoveResponseHeaders = CopyRemoveHeaders(RemoveResponseHeaders);
    }

    public IReadOnlyList<RuntimeHeaderFieldProjection> SetRequestHeaders { get; }
    public IReadOnlyList<string> RemoveRequestHeaders { get; }
    public IReadOnlyList<RuntimeHeaderFieldProjection> SetResponseHeaders { get; }
    public IReadOnlyList<string> RemoveResponseHeaders { get; }

    private static IReadOnlyList<RuntimeHeaderFieldProjection> CopySetHeaders(IEnumerable<RuntimeHeaderFieldProjection> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var copy = new List<RuntimeHeaderFieldProjection>();
        foreach (var header in headers)
        {
            ArgumentNullException.ThrowIfNull(header);
            copy.Add(header);
        }

        return new ReadOnlyCollection<RuntimeHeaderFieldProjection>(copy);
    }

    private static IReadOnlyList<string> CopyRemoveHeaders(IEnumerable<string> headerNames)
    {
        ArgumentNullException.ThrowIfNull(headerNames);
        var copy = new List<string>();
        foreach (var headerName in headerNames)
        {
            ProxyHeaderPolicyFacts.ValidatePolicyHeaderName(headerName);
            copy.Add(headerName);
        }

        return new ReadOnlyCollection<string>(copy);
    }
}
