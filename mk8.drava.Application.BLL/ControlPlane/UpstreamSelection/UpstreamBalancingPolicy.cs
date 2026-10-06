namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

public sealed record UpstreamBalancingPolicy
{
    public UpstreamBalancingPolicy(BalancingAlgorithm algorithm, string? affinityHeader = null, string? preferredZone = null, bool requireLocalZone = false)
    {
        if (!Enum.IsDefined(algorithm)) throw new InvalidDataException("Unknown balancing algorithm.");
        if (algorithm == BalancingAlgorithm.StableHash && (string.IsNullOrEmpty(affinityHeader) || affinityHeader.Length > 128))
            throw new InvalidDataException("Hash balancing requires an explicit affinity header.");
        if (affinityHeader is not null)
            foreach (var character in affinityHeader)
                if (!char.IsAsciiLetterOrDigit(character) && character != '-') throw new InvalidDataException("Invalid affinity header.");
        if (preferredZone is not null) Registry.RegistryNames.RequireLabel(preferredZone);
        if (requireLocalZone && preferredZone is null) throw new InvalidDataException("Strict locality requires a zone.");
        Algorithm = algorithm;
        AffinityHeader = affinityHeader;
        PreferredZone = preferredZone;
        RequireLocalZone = requireLocalZone;
    }

    public static UpstreamBalancingPolicy Default { get; } = new(BalancingAlgorithm.PowerOfTwoChoices);
    public BalancingAlgorithm Algorithm { get; }
    public string? AffinityHeader { get; }
    public string? PreferredZone { get; }
    public bool RequireLocalZone { get; }

    public static UpstreamBalancingPolicy FromName(string name)
    {
        if (string.Equals(name, "p2c", StringComparison.OrdinalIgnoreCase)) return Default;
        if (string.Equals(name, "round-robin", StringComparison.OrdinalIgnoreCase)) return new(BalancingAlgorithm.WeightedRoundRobin);
        if (string.Equals(name, "least-active", StringComparison.OrdinalIgnoreCase)) return new(BalancingAlgorithm.LeastActive);
        throw new InvalidDataException("Balancing needs a supported effective policy.");
    }
}
