namespace Mk8.Drava.IntegrationTests;

// Subprocess-owning classes use one named xUnit collection. Their internal concurrency
// assertions still execute; unrelated integration collections keep the default scheduling.
internal static class DevelopmentSubprocessTests
{
    public const string Name = "Development subprocesses";
}
