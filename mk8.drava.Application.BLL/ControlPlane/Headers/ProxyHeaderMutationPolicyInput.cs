using Mk8.Drava.Application.BLL.Http;
using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Headers;
public sealed record ProxyHeaderMutationPolicyInput
{
    public ProxyHeaderMutationPolicyInput(IReadOnlyList<ProxyHeaderField> SetRequestHeaders, IReadOnlyList<string> RemoveRequestHeaders, IReadOnlyList<ProxyHeaderField> SetResponseHeaders, IReadOnlyList<string> RemoveResponseHeaders)
    {
        ArgumentNullException.ThrowIfNull(SetRequestHeaders);
        ArgumentNullException.ThrowIfNull(RemoveRequestHeaders);
        ArgumentNullException.ThrowIfNull(SetResponseHeaders);
        ArgumentNullException.ThrowIfNull(RemoveResponseHeaders);
        this.SetRequestHeaders = ProxyHeaderFieldList.Copy(SetRequestHeaders);
        this.RemoveRequestHeaders = CopyHeaderNames(RemoveRequestHeaders);
        this.SetResponseHeaders = ProxyHeaderFieldList.Copy(SetResponseHeaders);
        this.RemoveResponseHeaders = CopyHeaderNames(RemoveResponseHeaders);
    }

    public IReadOnlyList<ProxyHeaderField> SetRequestHeaders { get; }
    public IReadOnlyList<string> RemoveRequestHeaders { get; }
    public IReadOnlyList<ProxyHeaderField> SetResponseHeaders { get; }
    public IReadOnlyList<string> RemoveResponseHeaders { get; }

    private static ReadOnlyCollection<string> CopyHeaderNames(IReadOnlyList<string> names)
    {
        var copy = new List<string>();
        foreach (var name in names)
        {
            ArgumentNullException.ThrowIfNull(name);
            copy.Add(name);
        }

        return new ReadOnlyCollection<string>(copy);
    }
}
