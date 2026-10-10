namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public sealed class ProxyListenerReloadPlanner
{
    #pragma warning disable CA1822 // Preserve the instance contract of this constructor-injected policy service.
    public ProxyListenerReloadPlan CreatePlan(IReadOnlyDictionary<string, ProxyTcpListenerReloadTarget> currentTcpListeners, IReadOnlyDictionary<string, ProxyTcpListenerReloadTarget> desiredTcpListeners, IReadOnlyDictionary<string, ProxyQuicListenerReloadTarget> currentQuicListeners, IReadOnlyDictionary<string, ProxyQuicListenerReloadTarget> desiredQuicListeners)
    {
        ArgumentNullException.ThrowIfNull(currentTcpListeners);
        ArgumentNullException.ThrowIfNull(desiredTcpListeners);
        ArgumentNullException.ThrowIfNull(currentQuicListeners);
        ArgumentNullException.ThrowIfNull(desiredQuicListeners);
        return new ProxyListenerReloadPlan(BuildTcpListenerDiff(currentTcpListeners, desiredTcpListeners), BuildQuicListenerDiff(currentQuicListeners, desiredQuicListeners));
    }
    #pragma warning restore CA1822

    private static ProxyListenerDiff BuildTcpListenerDiff(IReadOnlyDictionary<string, ProxyTcpListenerReloadTarget> currentListeners, IReadOnlyDictionary<string, ProxyTcpListenerReloadTarget> desiredListeners)
    {
        List<string> added = [];
        List<string> removed = [];
        List<string> changed = [];
        List<string> unchanged = [];
        foreach (var key in desiredListeners.Keys)
        {
            if (!currentListeners.TryGetValue(key, out var existing))
            {
                added.Add(key);
                continue;
            }

            if (CanReuse(existing, desiredListeners[key]))
            {
                unchanged.Add(key);
            }
            else
            {
                changed.Add(key);
            }
        }

        foreach (var key in currentListeners.Keys)
        {
            if (!desiredListeners.ContainsKey(key))
            {
                removed.Add(key);
            }
        }

        return CreateDiff(added, removed, changed, unchanged);
    }

    private static ProxyListenerDiff BuildQuicListenerDiff(IReadOnlyDictionary<string, ProxyQuicListenerReloadTarget> currentListeners, IReadOnlyDictionary<string, ProxyQuicListenerReloadTarget> desiredListeners)
    {
        List<string> added = [];
        List<string> removed = [];
        List<string> changed = [];
        List<string> unchanged = [];
        foreach (var key in desiredListeners.Keys)
        {
            if (!currentListeners.TryGetValue(key, out var existing))
            {
                added.Add(key);
                continue;
            }

            if (!existing.Failed && CanReuse(existing, desiredListeners[key]))
            {
                unchanged.Add(key);
            }
            else
            {
                changed.Add(key);
            }
        }

        foreach (var key in currentListeners.Keys)
        {
            if (!desiredListeners.ContainsKey(key))
            {
                removed.Add(key);
            }
        }

        return CreateDiff(added, removed, changed, unchanged);
    }

    private static ProxyListenerDiff CreateDiff(List<string> added, List<string> removed, List<string> changed, List<string> unchanged)
    {
        return new ProxyListenerDiff(added.Order(StringComparer.OrdinalIgnoreCase).ToArray(), removed.Order(StringComparer.OrdinalIgnoreCase).ToArray(), changed.Order(StringComparer.OrdinalIgnoreCase).ToArray(), unchanged.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool CanReuse(ProxyTcpListenerReloadTarget current, ProxyTcpListenerReloadTarget next)
    {
        return string.Equals(current.Address, next.Address, StringComparison.OrdinalIgnoreCase) && current.Port == next.Port && string.Equals(current.Transport, next.Transport, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanReuse(ProxyQuicListenerReloadTarget current, ProxyQuicListenerReloadTarget next)
    {
        return string.Equals(current.Address, next.Address, StringComparison.OrdinalIgnoreCase) && current.Port == next.Port && string.Equals(current.Transport, next.Transport, StringComparison.OrdinalIgnoreCase) && string.Equals(current.Http3Enablement, next.Http3Enablement, StringComparison.OrdinalIgnoreCase);
    }
}
