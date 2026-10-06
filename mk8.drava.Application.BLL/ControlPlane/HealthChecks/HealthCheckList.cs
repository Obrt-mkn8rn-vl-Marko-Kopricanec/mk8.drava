using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
internal static class HealthCheckList
{
    public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.Select(RequireValue).ToArray());
    }

    private static T RequireValue<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }
}
