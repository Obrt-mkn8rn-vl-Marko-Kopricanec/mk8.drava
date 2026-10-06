using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
internal static class ConfigurationManagementList
{
    public static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> values)
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
