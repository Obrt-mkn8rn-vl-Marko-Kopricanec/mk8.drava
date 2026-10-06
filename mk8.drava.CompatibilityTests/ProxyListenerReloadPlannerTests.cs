using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyListenerReloadPlannerTests
{
    public static void ClassifiesTcpListenerDiff()
    {
        var planner = new ProxyListenerReloadPlanner();
        var plan = planner.CreatePlan(Tcp(new ProxyTcpListenerReloadTarget("main", "127.0.0.1", 5000, "Http"), new ProxyTcpListenerReloadTarget("removed", "127.0.0.1", 5001, "Http"), new ProxyTcpListenerReloadTarget("changed", "127.0.0.1", 5002, "Http")), Tcp(new ProxyTcpListenerReloadTarget("main", "127.0.0.1", 5000, "Http"), new ProxyTcpListenerReloadTarget("changed", "127.0.0.1", 5003, "Http"), new ProxyTcpListenerReloadTarget("added", "127.0.0.1", 5004, "Http")), Quic(), Quic());
        AssertSequence(plan.TcpDiff.Added, "added");
        AssertSequence(plan.TcpDiff.Removed, "removed");
        AssertSequence(plan.TcpDiff.Changed, "changed");
        AssertSequence(plan.TcpDiff.Unchanged, "main");
        AssertSequence(plan.QuicDiff.Added);
        AssertSequence(plan.QuicDiff.Removed);
        AssertSequence(plan.QuicDiff.Changed);
        AssertSequence(plan.QuicDiff.Unchanged);
    }

    public static void ReplacesFailedQuicListener()
    {
        var planner = new ProxyListenerReloadPlanner();
        var plan = planner.CreatePlan(Tcp(), Tcp(), Quic(new ProxyQuicListenerReloadTarget("main|quic", "127.0.0.1", 6000, "Https", "default", Failed: true), new ProxyQuicListenerReloadTarget("same|quic", "127.0.0.1", 6001, "Https", "default", Failed: false), new ProxyQuicListenerReloadTarget("removed|quic", "127.0.0.1", 6002, "Https", "default", Failed: false)), Quic(new ProxyQuicListenerReloadTarget("main|quic", "127.0.0.1", 6000, "Https", "default", Failed: false), new ProxyQuicListenerReloadTarget("same|quic", "127.0.0.1", 6001, "Https", "default", Failed: false), new ProxyQuicListenerReloadTarget("added|quic", "127.0.0.1", 6003, "Https", "default", Failed: false)));
        AssertSequence(plan.QuicDiff.Added, "added|quic");
        AssertSequence(plan.QuicDiff.Removed, "removed|quic");
        AssertSequence(plan.QuicDiff.Changed, "main|quic");
        AssertSequence(plan.QuicDiff.Unchanged, "same|quic");
    }

    public static void ListenerDiffCopiesInputLists()
    {
        var added = new List<string>
        {
            "added"
        };
        var removed = new List<string>
        {
            "removed"
        };
        var changed = new List<string>
        {
            "changed"
        };
        var unchanged = new List<string>
        {
            "unchanged"
        };
        var diff = new ProxyListenerDiff(added, removed, changed, unchanged);
        added.Clear();
        removed.Clear();
        changed.Clear();
        unchanged.Clear();
        AssertSequence(diff.Added, "added");
        AssertSequence(diff.Removed, "removed");
        AssertSequence(diff.Changed, "changed");
        AssertSequence(diff.Unchanged, "unchanged");
        AssertEx.False(diff.Added is string[], "Listener reload diff added list should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerDiff(null!, [], [], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerDiff([], null!, [], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerDiff([], [], null!, []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerDiff([], [], [], null!));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerDiff([null!], [], [], []));
        AssertEx.Throws<ArgumentNullException>(() => new ProxyListenerDiff([], [null!], [], []));
    }

    private static Dictionary<string, ProxyTcpListenerReloadTarget> Tcp(params ProxyTcpListenerReloadTarget[] targets)
    {
        return targets.ToDictionary(static target => target.Key, static target => target, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ProxyQuicListenerReloadTarget> Quic(params ProxyQuicListenerReloadTarget[] targets)
    {
        return targets.ToDictionary(static target => target.Key, static target => target, StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertSequence(IReadOnlyList<string> actual, params string[] expected)
    {
        AssertEx.Equal(string.Join("|", expected), string.Join("|", actual));
    }
}
