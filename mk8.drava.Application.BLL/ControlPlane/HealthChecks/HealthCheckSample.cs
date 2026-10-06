namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public sealed record HealthCheckSample
{
    private HealthCheckSample(bool healthy, string result)
    {
        Healthy = healthy;
        Result = result;
    }

    public bool Healthy { get; }
    public string Result { get; }

    public static HealthCheckSample HealthyResult(string result)
    {
        return new HealthCheckSample(healthy: true, result);
    }

    public static HealthCheckSample UnhealthyResult(string result)
    {
        return new HealthCheckSample(healthy: false, result);
    }

    public static HealthCheckSample FromHttpStatus(int statusCode)
    {
        return FromStatusCode("HTTP", statusCode);
    }

    public static HealthCheckSample FromHttp2Status(int statusCode)
    {
        return FromStatusCode("HTTP/2", statusCode);
    }

    public static HealthCheckSample FromHttp3Status(int statusCode)
    {
        return FromStatusCode("HTTP/3", statusCode);
    }

    private static HealthCheckSample FromStatusCode(string protocolLabel, int statusCode)
    {
        var result = $"{protocolLabel} {statusCode}";
        return statusCode is >= 200 and <= 399 ? HealthyResult(result) : UnhealthyResult(result);
    }
}
