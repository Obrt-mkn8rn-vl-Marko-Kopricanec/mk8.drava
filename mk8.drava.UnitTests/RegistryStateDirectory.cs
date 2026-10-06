namespace Mk8.Drava.UnitTests;

internal sealed class RegistryStateDirectory : IDisposable
{
    public RegistryStateDirectory()
    {
        Path = Directory.CreateTempSubdirectory("mk8-drava-registry-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public string Path { get; }
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
