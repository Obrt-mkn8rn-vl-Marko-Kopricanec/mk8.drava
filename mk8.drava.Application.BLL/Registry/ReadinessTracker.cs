namespace Mk8.Drava.Application.BLL.Registry;

public sealed class ReadinessTracker
{
    private readonly Dictionary<string, Observation> _observations = new(StringComparer.Ordinal);

    public bool Record(RegisteredUpstreamIdentity identity, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!_observations.TryGetValue(identity.Partition, out var observation)) _observations.Add(identity.Partition, observation = new Observation());
        if (succeeded)
        {
            observation.Successes = Math.Min(2, observation.Successes + 1);
            observation.Failures = 0;
            if (observation.Successes >= 2) observation.Ready = true;
        }
        else
        {
            observation.Failures = Math.Min(3, observation.Failures + 1);
            observation.Successes = 0;
            if (observation.Failures >= 3) observation.Ready = false;
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
