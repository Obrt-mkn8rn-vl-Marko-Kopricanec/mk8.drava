using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ProxyMetricLabelPolicyTests
{
    public static void NormalizesBoundedSafeLabelValues()
    {
        AssertEx.Equal("none", ProxyMetricLabelPolicy.NormalizeValue(null));
        AssertEx.Equal("none", ProxyMetricLabelPolicy.NormalizeValue(" \t "));
        AssertEx.Equal("site_secret", ProxyMetricLabelPolicy.NormalizeValue(" site\nsecret "));
        AssertEx.Equal("route_with_slash_and_query_token_secret", ProxyMetricLabelPolicy.NormalizeValue("route\"with/slash/and/query?token=secret"));
        AssertEx.Equal("abc-_.XYZ09", ProxyMetricLabelPolicy.NormalizeValue("abc-_.XYZ09"));
        var longValue = new string ('a', ProxyMetricLabelPolicy.MaxLabelLength + 10);
        var normalized = ProxyMetricLabelPolicy.NormalizeValue(longValue);
        AssertEx.Equal(ProxyMetricLabelPolicy.MaxLabelLength, normalized.Length);
        AssertEx.Equal(new string ('a', ProxyMetricLabelPolicy.MaxLabelLength), normalized);
    }

    public static void ClassifiesMetricStatusCodes()
    {
        AssertEx.Equal("none", ProxyMetricLabelPolicy.StatusClass(null));
        AssertEx.Equal("1xx", ProxyMetricLabelPolicy.StatusClass(100));
        AssertEx.Equal("2xx", ProxyMetricLabelPolicy.StatusClass(204));
        AssertEx.Equal("5xx", ProxyMetricLabelPolicy.StatusClass(599));
        AssertEx.Equal("other", ProxyMetricLabelPolicy.StatusClass(99));
        AssertEx.Equal("other", ProxyMetricLabelPolicy.StatusClass(600));
    }
}
