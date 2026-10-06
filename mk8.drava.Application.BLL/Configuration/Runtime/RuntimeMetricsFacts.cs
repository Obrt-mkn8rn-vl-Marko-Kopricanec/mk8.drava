namespace Mk8.Drava.Application.BLL.Configuration;
internal static class RuntimeMetricsFacts
{
    public static void ValidateEndpointPath(string endpointPath, string parameterName)
    {
        if (!string.Equals(endpointPath, RuntimeMetricsOptions.FixedAdminEndpointPath, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Metrics endpoint path must be {RuntimeMetricsOptions.FixedAdminEndpointPath}.", parameterName);
        }
    }
}
