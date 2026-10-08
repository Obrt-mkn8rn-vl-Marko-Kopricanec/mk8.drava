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
