namespace Mk8.Drava.CompatibilityTests;

// Each pin test owns and removes its generated PFX files, including failed assertion exits.
internal sealed class CertificatePinFixtureDirectory : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("mk8-drava-pins-");

    public string Path => _directory.FullName;

    public void Dispose() => _directory.Delete(recursive: true);
}
