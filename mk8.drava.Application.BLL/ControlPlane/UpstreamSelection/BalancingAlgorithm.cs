namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

public enum BalancingAlgorithm
{
    PowerOfTwoChoices,
    WeightedRoundRobin,
    LeastActive,
    StableHash,
}
