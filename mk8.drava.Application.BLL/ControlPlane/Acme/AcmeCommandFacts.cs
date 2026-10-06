namespace Mk8.Drava.Application.BLL.ControlPlane.Acme;
internal static class AcmeCommandFacts
{
    public static IReadOnlyList<string> CopyRequiredStrings(IEnumerable<string> values, string parameterName)
    {
        var copy = CopyStrings(values, parameterName);
        if (copy.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", parameterName);
        }

        return copy;
    }

    public static IReadOnlyList<string> CopyStrings(IEnumerable<string> values, string parameterName)
    {
        var copy = AcmeList.Copy(values);
        foreach (var value in copy)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Values cannot be empty.", parameterName);
            }
        }

        return copy;
    }

    public static byte[] CopyRequiredBytes(byte[] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one byte is required.", parameterName);
        }

        return values.ToArray();
    }
}
