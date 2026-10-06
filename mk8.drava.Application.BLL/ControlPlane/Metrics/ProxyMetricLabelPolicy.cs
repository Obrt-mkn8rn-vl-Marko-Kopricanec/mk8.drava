namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public static class ProxyMetricLabelPolicy
{
    public const int MaxLabelLength = 96;
    public const string EmptyLabelValue = "none";
    public static string StatusClass(int? statusCode)
    {
        if (!statusCode.HasValue)
        {
            return EmptyLabelValue;
        }

        var value = statusCode.Value;
        return value is >= 100 and <= 599 ? $"{value / 100}xx" : "other";
    }

    public static string NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EmptyLabelValue;
        }

        Span<char> buffer = stackalloc char[Math.Min(value.Length, MaxLabelLength)];
        var index = 0;
        foreach (var character in value.Trim())
        {
            if (index >= buffer.Length)
            {
                break;
            }

            buffer[index++] = IsSafeLabelCharacter(character) ? character : '_';
        }

        return index == 0 ? EmptyLabelValue : new string (buffer[..index]);
    }

    private static bool IsSafeLabelCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';
    }
}
