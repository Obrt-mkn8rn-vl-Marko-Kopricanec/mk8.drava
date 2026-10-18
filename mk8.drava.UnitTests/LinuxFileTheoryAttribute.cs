using Xunit;

namespace Mk8.Drava.UnitTests;

internal sealed class LinuxFileTheoryAttribute : TheoryAttribute
{
    public LinuxFileTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Requires the actual Linux private filesystem profile.";
    }
}
