using FluentAssertions;

namespace Blueprint.Migration.Analyzers.Tests;

public sealed class MigrationDiagnosticDescriptorsTests
{
    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_placeholder_id_reserves_the_first_bpmig_slot()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.PlaceholderProof.Id.Should().Be("BPMIG001");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.PlaceholderProof.Category.Should().Be("BlueprintMigration");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_placeholder_descriptor_stays_non_actionable_until_real_analyzers_land()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.PlaceholderProof.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.PlaceholderProof.IsEnabledByDefault.Should().BeFalse();
    }
}
