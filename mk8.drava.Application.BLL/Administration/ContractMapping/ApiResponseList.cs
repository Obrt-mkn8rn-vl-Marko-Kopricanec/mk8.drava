using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
internal static class ApiResponseList
{
    public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
