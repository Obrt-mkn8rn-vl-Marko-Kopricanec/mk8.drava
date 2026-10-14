using Xunit;

namespace Mk8.Drava.UnitTests;

internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires actual Windows execution; Linux results do not qualify this control.";
    }
}
