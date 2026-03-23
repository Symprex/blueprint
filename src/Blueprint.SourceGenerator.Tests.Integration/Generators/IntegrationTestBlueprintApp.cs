using Blueprint.Model;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

/// <summary>
/// Provides a minimal app shell for integration fixtures that need the generator to discover operations through the same
/// app-scan entry point used by real Blueprint consumers.
/// </summary>
[BlueprintAppScan]
public partial class IntegrationTestBlueprintApp : BlueprintApp;
