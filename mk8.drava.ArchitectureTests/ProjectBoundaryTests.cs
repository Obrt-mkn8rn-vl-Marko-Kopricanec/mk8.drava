using System.Xml.Linq;
using Xunit;

namespace Mk8.Drava.ArchitectureTests;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void RegistrationSdkExcludesApplicationImplementationAndKeepsAnInwardOnlyGraph()
    {
        var graph = ReadGraph();
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        Visit("mk8.drava.Registration", graph, reachable);
        Assert.DoesNotContain("mk8.drava.Application.BLL", reachable);
        Assert.DoesNotContain("mk8.drava.Application.DAL", reachable);
        Assert.DoesNotContain("mk8.drava.Application.INF", reachable);
        Assert.Subset(new HashSet<string>(["mk8.drava.Contracts", "mk8.drava.Configuration", "mk8.drava.Transport"], StringComparer.Ordinal), new HashSet<string>(graph["mk8.drava.Registration"], StringComparer.Ordinal));
    }

    [Fact]
    public void GatewayTransitiveGraphExcludesApplicationImplementationAndDataAccess()
    {
        var graph = ReadGraph();
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        Visit("mk8.drava.Gateway", graph, reachable);
        Assert.DoesNotContain("mk8.drava.Application.BLL", reachable);
        Assert.DoesNotContain("mk8.drava.Application.DAL", reachable);
        Assert.DoesNotContain("mk8.drava.Application.INF", reachable);
        Assert.DoesNotContain("mk8.drava.Application", reachable);
        Assert.Contains("mk8.drava.Presentation", reachable);
        Assert.Contains("mk8.drava.Transport", reachable);
    }

    [Fact]
    public void BusinessAndSharedContractsPointInward()
    {
        var graph = ReadGraph();
        Assert.Empty(graph["mk8.drava.Contracts"]);
        Assert.Empty(graph["mk8.drava.Configuration"]);
        Assert.Equal(["mk8.drava.Contracts"], graph["mk8.drava.Application.BLL"]);
        Assert.Subset(new HashSet<string>(["mk8.drava.Contracts", "mk8.drava.Configuration"], StringComparer.Ordinal),
            new HashSet<string>(graph["mk8.drava.Transport"], StringComparer.Ordinal));
        var bll = XDocument.Load(Path.Combine(FindRoot(), "mk8.drava.Application.BLL", "mk8.drava.Application.BLL.csproj"));
        Assert.Empty(bll.Descendants("FrameworkReference"));
        Assert.DoesNotContain(bll.Descendants("PackageReference"), static reference =>
            ((string?)reference.Attribute("Include"))?.StartsWith("Grpc.", StringComparison.Ordinal) == true
            || string.Equals((string?)reference.Attribute("Include"), "Microsoft.Data.Sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void ActualProjectsAreDirectRootChildrenAndNativeIngressIsTestOnly()
    {
        var root = FindRoot();
        var solution = XDocument.Load(Path.Combine(root, "mk8.drava.slnx"));
        foreach (var entry in solution.Descendants("Project"))
        {
            var path = Assert.IsType<string>((string?)entry.Attribute("Path"));
            Assert.Equal(2, path.Split('/').Length);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), path.Split('/')[0]);
            Assert.True(File.Exists(Path.Combine(root, path)));
        }
        foreach (var project in new[] { "mk8.drava.Gateway", "mk8.drava.Presentation", "mk8.drava.Application.INF", "mk8.drava.Application" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("CompatibilityTests.LegacyIngress", text, StringComparison.Ordinal);
                Assert.DoesNotContain("class ClientConnection", text, StringComparison.Ordinal);
                Assert.DoesNotContain("class Http2ClientConnection", text, StringComparison.Ordinal);
                Assert.DoesNotContain("class Http3Connection", text, StringComparison.Ordinal);
            }
    }

    private static Dictionary<string, string[]> ReadGraph()
    {
        var root = FindRoot();
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(file)), root, StringComparison.Ordinal)) continue;
            result.Add(Path.GetFileNameWithoutExtension(file), XDocument.Load(file).Descendants("ProjectReference")
                .Select(static reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include") ?? "")).ToArray());
        }
        return result;
    }

    private static void Visit(string name, IReadOnlyDictionary<string, string[]> graph, HashSet<string> seen)
    {
        if (!seen.Add(name)) return;
        foreach (var dependency in graph[name]) Visit(dependency, graph, seen);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mk8.drava.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Development checkout root not found.");
    }
}
