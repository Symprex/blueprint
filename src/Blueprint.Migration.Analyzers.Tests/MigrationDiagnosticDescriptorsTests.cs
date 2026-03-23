using FluentAssertions;

namespace Blueprint.Migration.Analyzers.Tests;

public sealed class MigrationDiagnosticDescriptorsTests
{
    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_redundant_addopenapi_activation_uses_the_first_bpmig_slot()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.Id.Should().Be("BPMIG001");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.Category.Should().Be("BlueprintMigration");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_redundant_addopenapi_activation_is_enabled_and_actionable()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.MessageFormat.ToString().Should().Contain("AddOpenApi()");
    }
}
