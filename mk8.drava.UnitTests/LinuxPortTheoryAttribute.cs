using Xunit;

namespace Mk8.Drava.UnitTests;

internal sealed class LinuxPortTheoryAttribute : TheoryAttribute
{
    public LinuxPortTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "This regression checks the Linux kernel's automatic client-port range.";
    }
}
