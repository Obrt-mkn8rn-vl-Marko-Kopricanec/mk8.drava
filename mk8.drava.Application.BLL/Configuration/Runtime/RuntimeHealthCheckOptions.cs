namespace Mk8.Drava.Application.BLL.Configuration;
public sealed record RuntimeHealthCheckOptions
{
    public RuntimeHealthCheckOptions(bool Enabled, string Path, TimeSpan Interval, TimeSpan Timeout, int HealthyThreshold, int UnhealthyThreshold)
    {
        RuntimeHealthCheckFacts.Validate(Path, Interval, Timeout, HealthyThreshold, UnhealthyThreshold);
        this.Enabled = Enabled;
        this.Path = Path;
        this.Interval = Interval;
        this.Timeout = Timeout;
        this.HealthyThreshold = HealthyThreshold;
        this.UnhealthyThreshold = UnhealthyThreshold;
    }

    public bool Enabled { get; }
    public string Path { get; }
    public TimeSpan Interval { get; }
    public TimeSpan Timeout { get; }
    public int HealthyThreshold { get; }
    public int UnhealthyThreshold { get; }
}
