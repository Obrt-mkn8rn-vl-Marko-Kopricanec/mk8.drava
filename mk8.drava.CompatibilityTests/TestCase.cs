namespace Mk8.Drava.CompatibilityTests;

internal sealed record TestCase(string Name, Func<Task> Run, IReadOnlySet<string> Categories);
