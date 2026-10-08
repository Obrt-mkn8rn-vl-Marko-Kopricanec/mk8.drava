using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeList
{
    public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new List<T>();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(values));
            copy.Add(value);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    public static ReadOnlyDictionary<TKey, TValue> CopyDictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> values, IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(values, comparer));
    }
}
