using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Blueprint.Migration.Analyzers.Tests;

public sealed class MigrationReportSerializationTests
{
    [Xunit.Fact]
    public void When_serializing_activation_migration_report_then_emits_stable_bucketed_json()
    {
        var report = MigrationReport.CreateMigrationDiagnostics(
            [
                MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration,
                MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation,
                MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration,
            ]);

        var json = JsonSerializer.Serialize(report);
        var document = JsonNode.Parse(json);

        document.Should().NotBeNull();
        document!["SchemaVersion"]!.GetValue<int>().Should().Be(1);
        document["ReportType"]!.GetValue<string>().Should().Be("migration-diagnostics");

        var findings = document["Findings"]!.AsArray();
        findings.Select(static node => node!["DiagnosticId"]!.GetValue<string>())
            .Should()
            .Equal("BPMIG001", "BPMIG100", "BPMIG101");

        findings[0]!["Category"]!.GetValue<string>().Should().Be("auto-fixable");
        findings[1]!["Category"]!.GetValue<string>().Should().Be("configuration required");
        findings[2]!["Category"]!.GetValue<string>().Should().Be("configuration required");
    }

    [Xunit.Fact]
    public void When_creating_activation_migration_report_then_points_findings_at_the_relevant_migration_plan()
    {
        var report = MigrationReport.CreateMigrationDiagnostics(
            [
                MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration,
                MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation,
                MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation,
                MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation,
                MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration,
            ]);

        report.Findings.Select(static finding => (finding.DiagnosticId, finding.DocumentationPath))
            .Should()
            .Equal(
                ("BPMIG001", @"migration\openapi\plan.md"),
                ("BPMIG002", @"migration\resource-model-links-and-post-processing\plan.md"),
                ("BPMIG003", @"migration\authentication-and-authorisation\plan.md"),
                ("BPMIG100", @"migration\openapi\plan.md"),
                ("BPMIG101", @"migration\authentication-and-authorisation\plan.md"));
    }

    [Xunit.Fact]
    public void When_projecting_bpmig200_then_it_uses_the_authentication_migration_plan_and_consumer_extension_bucket()
    {
        var descriptor = new Microsoft.CodeAnalysis.DiagnosticDescriptor(
            id: "BPMIG200",
            title: "Migrate FromResource usage",
            messageFormat: "Migrate 'FromResource(...)' usage.",
            category: "BlueprintMigration",
            defaultSeverity: Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        var finding = DiagnosticFinding.FromDescriptor(descriptor);

        finding.Category.Should().Be("consumer-extension rewrite required");
        finding.DocumentationPath.Should().Be(@"migration\authentication-and-authorisation\plan.md");
    }

    [Xunit.Fact]
    public void When_projecting_bpmig201_then_it_uses_the_authentication_migration_plan_and_consumer_extension_bucket()
    {
        var descriptor = new Microsoft.CodeAnalysis.DiagnosticDescriptor(
            id: "BPMIG201",
            title: "Migrate auth placeholder usage",
            messageFormat: "Migrate '{Auth:...}' placeholder usage.",
            category: "BlueprintMigration",
            defaultSeverity: Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        var finding = DiagnosticFinding.FromDescriptor(descriptor);

        finding.Category.Should().Be("consumer-extension rewrite required");
        finding.DocumentationPath.Should().Be(@"migration\authentication-and-authorisation\plan.md");
    }

    [Xunit.Fact]
    public void When_validating_the_shared_migration_report_json_contract_then_the_payload_matches_the_explicit_schema()
    {
        var report = MigrationReport.CreateMigrationDiagnostics(
            [
                MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation,
                MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration,
                MigrationDiagnosticDescriptors.LegacyFromResourceUsageRequiresMigration,
                MigrationDiagnosticDescriptors.LegacyAuthPlaceholderUsageRequiresMigration,
            ]);

        var document = JsonNode.Parse(JsonSerializer.Serialize(report));

        document.Should().NotBeNull();
        AssertMatchesMigrationReportJsonContract(document!);
    }

    private static void AssertMatchesMigrationReportJsonContract(JsonNode document)
    {
        var root = document.AsObject();
        root.Select(static property => property.Key)
            .Should()
            .BeEquivalentTo(["SchemaVersion", "ReportType", "Findings"]);

        root["SchemaVersion"]!.GetValue<int>().Should().Be(1);
        root["ReportType"]!.GetValue<string>().Should().Be("migration-diagnostics");

        var findings = root["Findings"]!.AsArray();
        findings.Should().NotBeEmpty();

        foreach (var findingNode in findings)
        {
            findingNode.Should().NotBeNull();
            var finding = findingNode!.AsObject();
            finding.Select(static property => property.Key)
                .Should()
                .BeEquivalentTo(
                    [
                        "DiagnosticId",
                        "Title",
                        "Message",
                        "Description",
                        "Severity",
                        "Category",
                        "DocumentationPath",
                    ]);

            finding["DiagnosticId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            finding["Title"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            finding["Message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            finding["Description"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            finding["Severity"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            finding["Category"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            finding["DocumentationPath"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }
    }
}
