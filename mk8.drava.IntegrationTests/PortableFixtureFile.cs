namespace Mk8.Drava.IntegrationTests;

// Only disposable synthetic provider inputs. Production collection loading never uses this helper.
internal static class PortableFixtureFile
{
    public static async Task WriteAsync(string path, string document, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(path, document, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
