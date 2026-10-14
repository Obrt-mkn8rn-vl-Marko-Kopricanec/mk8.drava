namespace Mk8.Drava.Configuration;

internal static class WindowsFileNamePolicy
{
    internal static bool IsDeviceName(string component)
    {
        var dot = component.IndexOf('.', StringComparison.Ordinal);
        var name = (dot < 0 ? component : component[..dot]).TrimEnd(' ');
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || name.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                "123456789¹²³".Contains(name[3], StringComparison.Ordinal));
    }
}
