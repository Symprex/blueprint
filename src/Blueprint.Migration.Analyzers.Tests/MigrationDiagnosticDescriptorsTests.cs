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

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_redundant_addhateoaslinks_activation_is_enabled_and_actionable()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation.Id.Should().Be("BPMIG002");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation.MessageFormat.ToString().Should().Contain("AddHateoasLinks()");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_redundant_addauthorisation_activation_is_enabled_and_actionable()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation.Id.Should().Be("BPMIG003");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation.MessageFormat.ToString().Should().Contain("AddAuthorisation()");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_configured_addopenapi_uses_the_first_configuration_required_slot()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration.Id.Should().Be("BPMIG100");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration.MessageFormat.ToString().Should().Contain("ConfigureOpenApi");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_configured_addauthentication_uses_the_next_configuration_required_slot()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration.Id.Should().Be("BPMIG101");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration.MessageFormat.ToString().Should().Contain("AddAuthentication");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration.MessageFormat.ToString().Should().Contain("configuration");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_legacy_fromresource_uses_the_first_consumer_extension_slot()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyFromResourceUsageRequiresMigration.Id.Should().Be("BPMIG200");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyFromResourceUsageRequiresMigration.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyFromResourceUsageRequiresMigration.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyFromResourceUsageRequiresMigration.MessageFormat.ToString().Should().Contain("FromResource");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_legacy_auth_placeholder_uses_the_next_consumer_extension_slot()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyAuthPlaceholderUsageRequiresMigration.Id.Should().Be("BPMIG201");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyAuthPlaceholderUsageRequiresMigration.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyAuthPlaceholderUsageRequiresMigration.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyAuthPlaceholderUsageRequiresMigration.MessageFormat.ToString().Should().Contain("{Auth:");
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_legacy_namespace_uses_the_first_namespace_migration_slot()
    {
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyNamespaceUsing.Id.Should().Be("BPMIG300");
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyNamespaceUsing.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyNamespaceUsing.IsEnabledByDefault.Should().BeTrue();
        Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.LegacyNamespaceUsing.MessageFormat.ToString().Should().Contain("using");
    }
}
