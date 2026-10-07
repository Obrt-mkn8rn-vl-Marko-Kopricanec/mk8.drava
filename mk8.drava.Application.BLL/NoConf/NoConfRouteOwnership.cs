using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.NoConf;

internal static class NoConfRouteOwnership
{
    public static void Validate(IReadOnlyList<RuntimeRoute> manual, IReadOnlyList<RuntimeRoute> automatic)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hosts = new Dictionary<string, List<Claim>>(StringComparer.OrdinalIgnoreCase);
        var wildcard = new List<Claim>();
        foreach (var route in manual)
        {
            names.Add(route.Name);
            if (string.Equals(route.Host, "*", StringComparison.Ordinal)) wildcard.Add(new Claim(route, Automatic: false));
            else Add(route, automatic: false);
        }
        foreach (var route in automatic)
        {
            if (!names.Add(route.Name)) throw new InvalidDataException("Automatic route name conflicts with another route.");
            Add(route, automatic: true);
        }
        foreach (var claims in hosts.Values) ValidatePaths(claims, rejectAutomaticOverlap: true);
        if (wildcard.Count == 0) return;
        foreach (var route in automatic) wildcard.Add(new Claim(route, Automatic: true));
        ValidatePaths(wildcard, rejectAutomaticOverlap: false);

        void Add(RuntimeRoute route, bool automatic)
        {
            var host = route.Host;
            var colon = host.LastIndexOf(':');
            if (colon > 0 && !host.Contains(']', StringComparison.Ordinal)) host = host[..colon];
            if (!hosts.TryGetValue(host, out var claims)) hosts.Add(host, claims = []);
            claims.Add(new Claim(route, automatic));
        }
    }

    private static void ValidatePaths(List<Claim> claims, bool rejectAutomaticOverlap)
    {
        // The preserved matcher uses raw StartsWith, so /api also owns /api2.
        claims.Sort(static (left, right) => string.Compare(left.Route.PathPrefix, right.Route.PathPrefix, StringComparison.Ordinal));
        var ancestors = new Stack<Claim>();
        var automatic = 0;
        var manual = 0;
        for (var index = 0; index < claims.Count; index++)
        {
            var claim = claims[index];
            while (ancestors.TryPeek(out var prior) && !claim.Route.PathPrefix.StartsWith(prior.Route.PathPrefix, StringComparison.Ordinal))
            {
                if (ancestors.Pop().Automatic) automatic--;
                else manual--;
            }
            if (claim.Automatic ? manual > 0 || (rejectAutomaticOverlap && automatic > 0) : automatic > 0)
                throw new InvalidDataException("Automatic route path overlaps another service or a manual route; use distinct paths/hosts or explicit manual mode.");
            ancestors.Push(claim);
            if (claim.Automatic) automatic++;
            else manual++;
        }
    }

    private sealed record Claim(RuntimeRoute Route, bool Automatic);
}
