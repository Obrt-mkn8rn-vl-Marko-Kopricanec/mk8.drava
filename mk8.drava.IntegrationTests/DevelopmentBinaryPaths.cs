namespace Mk8.Drava.IntegrationTests;

internal static class DevelopmentBinaryPaths
{
    internal static string ForProject(string project)
    {
        if (project is not ("mk8.drava.Application" or "mk8.drava.Gateway"))
            throw new ArgumentException("Unknown development executable.", nameof(project));

        var testDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var testProject = testDirectory.Parent;
        if (testProject is not null &&
            string.Equals(testProject.Name, "mk8.drava.IntegrationTests", StringComparison.Ordinal) &&
            testProject.Parent is { } outputDirectory &&
            string.Equals(outputDirectory.Name, "bin", StringComparison.Ordinal))
        {
            return Path.Combine(outputDirectory.FullName, project, testDirectory.Name, project + ".dll");
        }

        return Path.Combine(TwoProcessProxy.FindRoot(), project, "bin", "Release", "net10.0", project + ".dll");
    }
}
