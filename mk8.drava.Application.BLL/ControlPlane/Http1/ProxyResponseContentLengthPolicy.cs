namespace Mk8.Drava.Application.BLL.ControlPlane.Http1;

public static class ProxyResponseContentLengthPolicy
{
    public static long? GetContentLength(Http1ResponseHead head, long? bufferedBodyBytes = null, bool isHeadResponse = false)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.StatusCode is >= 100 and < 200 or 204) return null;
        if (head.IsHeadResponse || isHeadResponse || head.StatusCode == 304)
        {
            var values = head.Headers.Where(static field => string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase))
                .Select(static field => field.Value).ToArray();
            if (values.Length == 0) return null;
            return Http1RequestParser.AnalyzeContentLength(values) switch
            {
                Http1ContentLengthAnalysisResult.Accepted accepted => accepted.ContentLength,
                _ => throw new InvalidDataException("Invalid bodyless representation length.")
            };
        }
        return bufferedBodyBytes ?? (head.Framing.Kind == Http1BodyKind.None ? 0 : head.Framing.ContentLength);
    }
}
