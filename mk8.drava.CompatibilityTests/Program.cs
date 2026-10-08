using System.Text.Json;
using Mk8.Drava.CompatibilityTests;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

if (PerformanceSmokeRunner.IsPerformanceCommand(args))
{
    Environment.ExitCode = await PerformanceSmokeRunner.RunAsync(args).ConfigureAwait(false);
    return;
}

var tests = TestRegistry.All;
TestRunOptions options;
try
{
    options = TestRunOptions.Parse(args);
}
catch (ArgumentException exception)
{
    await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
    await Console.Error.WriteLineAsync("Use --list-categories to see supported categories.").ConfigureAwait(false);
    Environment.ExitCode = 2;
    return;
}

if (options.ListCategories)
{
    foreach (var category in TestTaxonomy.Categories)
    {
        var count = tests.Count(test => test.Categories.Contains(category));
        Console.WriteLine($"{category} {count}");
    }

    return;
}

if (options.CheckMetadata)
{
    var metadataErrors = TestMetadataIntegrity.Validate(tests);
    if (metadataErrors.Count > 0)
    {
        await Console.Error.WriteLineAsync("Test metadata integrity check failed.").ConfigureAwait(false);
        foreach (var error in metadataErrors)
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
        }

        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine("Test metadata integrity check passed.");
    foreach (var category in TestTaxonomy.Categories)
    {
        var count = tests.Count(test => test.Categories.Contains(category));
        Console.WriteLine($"{category} {count}");
    }

    return;
}

var selectedTests = options.Categories.Count == 0 ? tests : tests.Where(test => test.Categories.Any(options.Categories.Contains)).ToArray();
if (selectedTests.Length == 0)
{
    await Console.Error.WriteLineAsync($"No tests matched categories: {string.Join(", ", options.Categories)}").ConfigureAwait(false);
    Environment.ExitCode = 2;
    return;
}

if (options.Categories.Count > 0)
{
    Console.WriteLine($"Running {selectedTests.Length} of {tests.Length} tests for categories: {string.Join(", ", options.Categories)}.");
}

var failures = 0;
List<string> failureNames = [];
foreach (var test in selectedTests)
{
    try
    {
        await test.Run().ConfigureAwait(false);
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        failureNames.Add(test.Name);
        await Console.Error.WriteLineAsync($"FAIL {test.Name}").ConfigureAwait(false);
        await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
    }
}

WriteCorrectnessSummary(options, tests.Length, selectedTests.Length, failures, failureNames);
if (failures > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Passed {selectedTests.Length} tests.");
static void WriteCorrectnessSummary(TestRunOptions options, int totalTests, int selectedTests, int failures, IReadOnlyList<string> failureNames)
{
    if (string.IsNullOrWhiteSpace(options.SummaryFile))
    {
        return;
    }

    var directory = Path.GetDirectoryName(options.SummaryFile);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var summary = new
    {
        kind = "correctness",
        status = failures == 0 ? "passed" : "failed",
        categories = options.Categories,
        totalTests,
        selectedTests,
        passedTests = selectedTests - failures,
        failedTests = failures,
        failures = failureNames
    };
    File.WriteAllText(options.SummaryFile, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
}

internal sealed record TestCase(string Name, Func<Task> Run, IReadOnlySet<string> Categories);
internal sealed record TestRunOptions(IReadOnlySet<string> Categories, bool ListCategories, bool CheckMetadata, string? SummaryFile)
{
    public static TestRunOptions Parse(string[] args)
    {
        HashSet<string> categories = new(StringComparer.OrdinalIgnoreCase);
        var listCategories = false;
        var checkMetadata = false;
        string? summaryFile = null;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--list-categories", StringComparison.OrdinalIgnoreCase))
            {
                listCategories = true;
                continue;
            }

            if (string.Equals(arg, "--check-test-metadata", StringComparison.OrdinalIgnoreCase))
            {
                checkMetadata = true;
                continue;
            }

            if (string.Equals(arg, "--summary-file", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"{arg} requires a file path.");
                }

                summaryFile = args[++index];
                continue;
            }

            if (string.Equals(arg, "--category", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--categories", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"{arg} requires a category value.");
                }

                AddCategories(args[++index], categories);
                continue;
            }

            const string categoryPrefix = "--category=";
            const string categoriesPrefix = "--categories=";
            const string summaryFilePrefix = "--summary-file=";
            if (arg.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddCategories(arg[categoryPrefix.Length..], categories);
                continue;
            }

            if (arg.StartsWith(categoriesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddCategories(arg[categoriesPrefix.Length..], categories);
                continue;
            }

            if (arg.StartsWith(summaryFilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                summaryFile = arg[summaryFilePrefix.Length..];
                continue;
            }

            throw new ArgumentException($"Unknown test runner argument: {arg}");
        }

        var canonical = categories.Select(TestTaxonomy.CanonicalCategory).OrderBy(static category => category, StringComparer.Ordinal).ToArray();
        return new TestRunOptions(canonical.ToHashSet(StringComparer.Ordinal), listCategories, checkMetadata, summaryFile);
    }

    private static void AddCategories(string value, HashSet<string> categories)
    {
        foreach (var category in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TestTaxonomy.IsKnownCategory(category))
            {
                throw new ArgumentException($"Unknown test category: {category}");
            }

            categories.Add(category);
        }
    }
}

internal static class TestTaxonomy
{
    public const string Http1 = "HTTP1";
    public const string Http2 = "HTTP2";
    public const string Http3 = "HTTP3";
    public const string UpstreamHttp1 = "UpstreamHTTP1";
    public const string UpstreamHttp2 = "UpstreamHTTP2";
    public const string UpstreamHttp3 = "UpstreamHTTP3";
    public const string Config = "Config";
    public const string Routing = "Routing";
    public const string Tls = "TLS";
    public const string Headers = "Headers";
    public const string Caching = "Caching";
    public const string RetryCircuit = "RetryCircuit";
    public const string HealthChecks = "HealthChecks";
    public const string Limits = "Limits";
    public const string Admin = "Admin";
    public const string Metrics = "Metrics";
    public const string SecurityNegativePaths = "SecurityNegativePaths";
    public static readonly string[] Categories = [Http1, Http2, Http3, UpstreamHttp1, UpstreamHttp2, UpstreamHttp3, Config, Routing, Tls, Headers, Caching, RetryCircuit, HealthChecks, Limits, Admin, Metrics, SecurityNegativePaths];
    private static readonly Dictionary<string, string> CategoryLookup = Categories.ToDictionary(static category => category, static category => category, StringComparer.OrdinalIgnoreCase);
    public static bool IsKnownCategory(string category)
    {
        return CategoryLookup.ContainsKey(category);
    }

    public static string CanonicalCategory(string category)
    {
        return CategoryLookup.TryGetValue(category, out var canonical) ? canonical : throw new ArgumentException($"Unknown test category: {category}");
    }

    public static IReadOnlySet<string> CanonicalCategories(params string[] categories)
    {
        HashSet<string> canonical = new(StringComparer.Ordinal);
        foreach (var category in categories)
        {
            canonical.Add(CanonicalCategory(category));
        }

        if (canonical.Count == 0)
        {
            throw new ArgumentException("Each test registration must declare at least one correctness category.");
        }

        return canonical;
    }
}

internal static class TestMetadataIntegrity
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<TestCase> tests)
    {
        List<string> errors = [];
        foreach (var test in tests)
        {
            if (test.Categories.Count == 0)
            {
                errors.Add($"Test has no correctness category: {test.Name}");
            }

            foreach (var category in test.Categories)
            {
                if (!TestTaxonomy.IsKnownCategory(category))
                {
                    errors.Add($"Test uses unknown correctness category '{category}': {test.Name}");
                }
            }
        }

        foreach (var category in TestTaxonomy.Categories)
        {
            if (!tests.Any(test => test.Categories.Contains(category)))
            {
                errors.Add($"Correctness category has zero tests: {category}");
            }
        }

        var duplicateNames = tests.GroupBy(static test => test.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1).Select(static group => group.Key);
        foreach (var name in duplicateNames)
        {
            errors.Add($"Duplicate test name: {name}");
        }

        return errors;
    }
}
