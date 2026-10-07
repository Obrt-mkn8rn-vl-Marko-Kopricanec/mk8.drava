namespace Mk8.Drava.Application.BLL.Registry;

public sealed class ReadinessTracker
{
    private readonly Dictionary<string, Observation> _observations = new(StringComparer.Ordinal);
    private readonly int _successes;
    private readonly int _failures;

    public ReadinessTracker() : this(2, 3) { }
    public ReadinessTracker(int successes, int failures)
    {
        if (successes is < 1 or > 20 || failures is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(successes), "Invalid readiness hysteresis.");
        _successes = successes; _failures = failures;
    }

    public bool Record(RegisteredUpstreamIdentity identity, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!_observations.TryGetValue(identity.Partition, out var observation)) _observations.Add(identity.Partition, observation = new Observation());
        if (succeeded)
        {
            observation.Successes = Math.Min(_successes, observation.Successes + 1);
            observation.Failures = 0;
            if (observation.Successes >= _successes) observation.Ready = true;
        }
        else
        {
            observation.Failures = Math.Min(_failures, observation.Failures + 1);
            observation.Successes = 0;
            if (observation.Failures >= _failures) observation.Ready = false;
        }
        return observation.Ready;
    }

    public void Retain(IReadOnlyList<RegisteredUpstreamIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identity in identities) retained.Add(identity.Partition);
        var removed = new List<string>();
        foreach (var key in _observations.Keys)
            if (!retained.Contains(key)) removed.Add(key);
        for (var index = 0; index < removed.Count; index++) _observations.Remove(removed[index]);
    }

    private sealed class Observation { public int Successes { get; set; } public int Failures { get; set; } public bool Ready { get; set; } }
}
