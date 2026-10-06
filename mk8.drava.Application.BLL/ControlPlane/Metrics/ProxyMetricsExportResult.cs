namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public abstract record ProxyMetricsExportResult
{
    private ProxyMetricsExportResult()
    {
    }

    public static ProxyMetricsExportResult Unavailable { get; } = new UnavailableResult();

    public static ProxyMetricsExportResult Exported(string content, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        return new ExportedResult(content, contentType);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record ExportedResult : ProxyMetricsExportResult
    {
        public ExportedResult(string content, string contentType)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
            Content = content;
            ContentType = contentType;
        }

        public string Content { get; }
        public string ContentType { get; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034", Justification = "Nested immutable cases form the closed domain result union; keeping cases qualified by their result preserves exhaustive pattern matching and the imported contract.")]
    public sealed record UnavailableResult : ProxyMetricsExportResult;
}
