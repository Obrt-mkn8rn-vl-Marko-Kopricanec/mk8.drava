using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
internal static class MetricsList
{
    public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.Select(RequireValue).ToArray());
    }

    public static ReadOnlyDictionary<TKey, TValue> CopyDictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> values, IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(values, comparer));
    }

    public static ReadOnlyDictionary<TKey, long> CopyCounterDictionary<TKey>(IReadOnlyDictionary<TKey, long> values, IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<TKey, long>(comparer);
        foreach (var item in values)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(item.Value);
            copy.Add(item.Key, item.Value);
        }

        return new ReadOnlyDictionary<TKey, long>(copy);
    }

    public static long RequireCounter(long value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        return value;
    }

    private static T RequireValue<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }
}
