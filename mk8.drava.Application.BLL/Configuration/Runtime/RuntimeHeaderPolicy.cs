using Mk8.Drava.Application.BLL.Http;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHeaderPolicy
{
    public RuntimeHeaderPolicy(IEnumerable<ProxyHeaderField> SetRequestHeaders, IEnumerable<string> RemoveRequestHeaders, IEnumerable<ProxyHeaderField> SetResponseHeaders, IEnumerable<string> RemoveResponseHeaders)
    {
        this.SetRequestHeaders = CopySetHeaders(SetRequestHeaders);
        this.RemoveRequestHeaders = CopyRemoveHeaders(RemoveRequestHeaders);
        this.SetResponseHeaders = CopySetHeaders(SetResponseHeaders);
        this.RemoveResponseHeaders = CopyRemoveHeaders(RemoveResponseHeaders);
    }

    public IReadOnlyList<ProxyHeaderField> SetRequestHeaders { get; }
    public IReadOnlyList<string> RemoveRequestHeaders { get; }
    public IReadOnlyList<ProxyHeaderField> SetResponseHeaders { get; }
    public IReadOnlyList<string> RemoveResponseHeaders { get; }
    public static RuntimeHeaderPolicy Empty { get; } = new([], [], [], []);

    private static IReadOnlyList<ProxyHeaderField> CopySetHeaders(IEnumerable<ProxyHeaderField> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var copy = new List<ProxyHeaderField>();
        foreach (var header in headers)
        {
            ArgumentNullException.ThrowIfNull(header);
            ProxyHeaderPolicyFacts.ValidateSetHeader(header.Name, header.Value);
            copy.Add(header);
        }

        return new ReadOnlyCollection<ProxyHeaderField>(copy);
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
