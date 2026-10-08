using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.Http;
internal static class ProxyHeaderFieldList
{
    public static IReadOnlyList<ProxyHeaderField> Copy(IEnumerable<ProxyHeaderField> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var copy = new List<ProxyHeaderField>();
        foreach (var header in headers)
        {
            ArgumentNullException.ThrowIfNull(header, nameof(headers));
            copy.Add(header);
        }

        return new ReadOnlyCollection<ProxyHeaderField>(copy);
    }
}
