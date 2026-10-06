using System.Xml.Linq;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class ArchitectureBoundaryTests
{
    public static void ProjectReferencesPreserveLayeredDependencyDirection()
    {
        var root = FindRepositoryRoot();
        AssertReferences(root, "mk8.drava.Application.BLL/mk8.drava.Application.BLL.csproj", ["mk8.drava.Contracts/mk8.drava.Contracts.csproj"]);
        AssertReferences(root, "mk8.drava.Application.INF/mk8.drava.Application.INF.csproj", ["mk8.drava.Application.BLL/mk8.drava.Application.BLL.csproj", "mk8.drava.Application.DAL/mk8.drava.Application.DAL.csproj", "mk8.drava.Contracts/mk8.drava.Contracts.csproj", "mk8.drava.Transport/mk8.drava.Transport.csproj"]);
        AssertReferences(root, "mk8.drava.Application.DAL/mk8.drava.Application.DAL.csproj", ["mk8.drava.Application.BLL/mk8.drava.Application.BLL.csproj", "mk8.drava.Contracts/mk8.drava.Contracts.csproj"]);
        AssertReferences(root, "mk8.drava.Contracts/mk8.drava.Contracts.csproj", []);
    }

    public static void BusinessLayerSourceDoesNotReferenceOuterLayers()
    {
        var root = FindRepositoryRoot();
        AssertSourceDoesNotContain(root, "mk8.drava.Application.BLL", ["Mk8.Drava.Gateway", "Mk8.Drava.Application.INF", "Mk8.Drava.Application.DAL", "Mk8.Drava.Transport", "Microsoft.AspNetCore", "Microsoft.Extensions.Hosting", "Grpc."]);
    }

    public static void InfrastructureSourceDoesNotReferenceApiLayer()
    {
        var root = FindRepositoryRoot();
        AssertSourceDoesNotContain(root, "mk8.drava.Application.INF", ["Mk8.Drava.Gateway", "Microsoft.AspNetCore"]);
    }

    public static void ApiControllersDoNotReferenceInfrastructureLayer()
    {
        var root = FindRepositoryRoot();
        AssertSourceDoesNotContain(root, "mk8.drava.Contracts", ["Mk8.Drava.Application"]);
    }

    public static void RuntimeConfigurationModelsDoNotExposeInitSetters()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, NormalizePath("mk8.drava.Application.BLL/Configuration/Runtime"));
        var violations = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(static path => !IsGeneratedOrBuildOutput(path)).SelectMany(path => InitSettersInFile(root, path)).OrderBy(static violation => violation, StringComparer.OrdinalIgnoreCase).ToArray();
        AssertEx.Equal("", string.Join(Environment.NewLine, violations));
    }

    private static void AssertReferences(string root, string projectPath, IReadOnlyList<string> expectedReferences)
    {
        var projectFile = Path.Combine(root, NormalizePath(projectPath));
        var actual = XDocument.Load(projectFile).Descendants("ProjectReference").Select(static element => element.Attribute("Include")?.Value).Where(static value => !string.IsNullOrWhiteSpace(value)).Select(value => NormalizeProjectReference(root, projectFile, value!)).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var expected = expectedReferences.Select(NormalizePath).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        AssertEx.Equal(string.Join("|", expected), string.Join("|", actual));
    }

    private static void AssertSourceDoesNotContain(string root, string projectDirectory, IReadOnlyList<string> forbiddenTokens)
    {
        var directory = Path.Combine(root, NormalizePath(projectDirectory));
        var violations = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(static path => !IsGeneratedOrBuildOutput(path)).SelectMany(path => ForbiddenTokensInFile(root, path, forbiddenTokens)).OrderBy(static violation => violation, StringComparer.OrdinalIgnoreCase).ToArray();
        AssertEx.Equal("", string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> ForbiddenTokensInFile(string root, string path, IReadOnlyList<string> forbiddenTokens)
    {
        var text = File.ReadAllText(path);
        foreach (var token in forbiddenTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
            {
                yield return $"{NormalizePath(Path.GetRelativePath(root, path))} contains forbidden boundary token '{token}'.";
            }
        }
    }

    private static IEnumerable<string> InitSettersInFile(string root, string path)
    {
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (line.Contains("init;", StringComparison.Ordinal))
            {
                yield return $"{NormalizePath(Path.GetRelativePath(root, path))}:{lineNumber} exposes an init setter in runtime configuration.";
            }
        }
    }

    private static bool IsGeneratedOrBuildOutput(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        }

        )
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "mk8.drava.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not find mk8.drava.slnx from the current test process.");
    }

    private static string NormalizeProjectReference(string root, string projectFile, string include)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile) ?? throw new InvalidOperationException($"Project path has no directory: {projectFile}");
        var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, include));
        return NormalizePath(Path.GetRelativePath(root, fullPath));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
