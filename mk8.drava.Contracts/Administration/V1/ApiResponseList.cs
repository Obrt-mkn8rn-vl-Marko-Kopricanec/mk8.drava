using System.Collections.ObjectModel;

namespace Mk8.Drava.Contracts.Administration.V1;
internal static class ApiResponseList
{
    public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
