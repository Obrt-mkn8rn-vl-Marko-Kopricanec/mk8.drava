using System.Collections.ObjectModel;

namespace Mk8.Drava.Application.BLL.ControlPlane.Resilience;
public sealed record CircuitBreakerPolicyInput
{
    public CircuitBreakerPolicyInput(bool Enabled, int FailureThreshold, TimeSpan SamplingWindow, TimeSpan OpenDuration, int HalfOpenMaxAttempts, IReadOnlyList<int> FailureStatusCodes)
    {
        ArgumentNullException.ThrowIfNull(FailureStatusCodes);
        this.Enabled = Enabled;
        this.FailureThreshold = FailureThreshold;
        this.SamplingWindow = SamplingWindow;
        this.OpenDuration = OpenDuration;
        this.HalfOpenMaxAttempts = HalfOpenMaxAttempts;
        this.FailureStatusCodes = new ReadOnlyCollection<int>(FailureStatusCodes.ToArray());
    }

    public bool Enabled { get; }
    public int FailureThreshold { get; }
    public TimeSpan SamplingWindow { get; }
    public TimeSpan OpenDuration { get; }
    public int HalfOpenMaxAttempts { get; }
    public IReadOnlyList<int> FailureStatusCodes { get; }
    public static CircuitBreakerPolicyInput Disabled { get; } = new(false, 5, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), 1, []);
}
