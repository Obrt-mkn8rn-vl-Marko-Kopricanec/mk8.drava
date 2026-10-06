namespace Mk8.Drava.Application.BLL.Configuration;
public sealed class HealthCheckOptions
{
    public bool Enabled { get; init; }
    public string Path { get; init; } = "/health";
    public int IntervalSeconds { get; init; } = 10;
    public int TimeoutSeconds { get; init; } = 2;
    public int HealthyThreshold { get; init; } = 2;
    public int UnhealthyThreshold { get; init; } = 2;
}
