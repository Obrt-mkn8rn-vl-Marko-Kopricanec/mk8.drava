namespace Mk8.Drava.CompatibilityTests;

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
                    throw new ArgumentException($"{arg} requires a file path.", nameof(args));
                }

                summaryFile = args[++index];
                continue;
            }

            if (string.Equals(arg, "--category", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--categories", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"{arg} requires a category value.", nameof(args));
                }

                AddCategories(args[++index], categories);
                continue;
            }

            if (TryReadInlineOption(arg, categories, ref summaryFile))
            {
                continue;
            }

            throw new ArgumentException($"Unknown test runner argument: {arg}", nameof(args));
        }

        var canonical = categories.Select(TestTaxonomy.CanonicalCategory).OrderBy(static category => category, StringComparer.Ordinal).ToArray();
        return new TestRunOptions(canonical.ToHashSet(StringComparer.Ordinal), listCategories, checkMetadata, summaryFile);
    }

    private static bool TryReadInlineOption(string arg, HashSet<string> categories, ref string? summaryFile)
    {
        const string categoryPrefix = "--category=";
        const string categoriesPrefix = "--categories=";
        const string summaryFilePrefix = "--summary-file=";
        if (arg.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            AddCategories(arg[categoryPrefix.Length..], categories);
            return true;
        }

        if (arg.StartsWith(categoriesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            AddCategories(arg[categoriesPrefix.Length..], categories);
            return true;
        }

        if (arg.StartsWith(summaryFilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            summaryFile = arg[summaryFilePrefix.Length..];
            return true;
        }

        return false;
    }

    private static void AddCategories(string value, HashSet<string> categories)
    {
        foreach (var category in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TestTaxonomy.IsKnownCategory(category))
            {
                throw new ArgumentException($"Unknown test category: {category}", nameof(value));
            }

            categories.Add(category);
        }
    }
}
