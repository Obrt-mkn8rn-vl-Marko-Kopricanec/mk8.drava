using Xunit;

namespace Mk8.Drava.UnitTests;

[CollectionDefinition("Portable configuration environment", DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515", Justification = "xUnit1027 requires a public collection definition for framework discovery; this marker serializes process-environment controls against other collections.")]
public sealed class PortableConfigurationEnvironmentTests;
