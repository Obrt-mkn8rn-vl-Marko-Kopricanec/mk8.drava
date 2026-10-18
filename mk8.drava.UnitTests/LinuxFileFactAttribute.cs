using Xunit;

namespace Mk8.Drava.UnitTests;

internal sealed class LinuxFileFactAttribute : FactAttribute
{
    public LinuxFileFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Requires the actual Linux private filesystem profile.";
    }
}
