namespace Mk8.Drava.CompatibilityTests;

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
