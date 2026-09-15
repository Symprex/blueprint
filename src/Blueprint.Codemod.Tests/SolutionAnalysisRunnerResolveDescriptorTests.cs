using System.Reflection;
using Blueprint.Migration.Analyzers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.Codemod.Tests;

/// <summary>
/// Guards against a diagnostic family that exists in <see cref="MigrationDiagnosticDescriptors"/> but has no
/// matching arm in <see cref="SolutionAnalysisRunner.ResolveDescriptor"/>. A missing arm makes
/// <c>ResolveDescriptor</c> return <see langword="null"/> for every instance of that diagnostic, which is silently
/// dropped from <c>DryRunAsync</c>'s report before <c>Findings</c> is built — so the diagnostic never reaches the
/// dry-run report, <c>--mode review</c> output, or <c>codemod-report.json</c>/<c>.md</c>, no matter how many sites
/// it fires on. This exact gap let BPMIG203 and BPMIG402 both go unreported.
/// </summary>
public sealed class SolutionAnalysisRunnerResolveDescriptorTests
{
    [Xunit.Fact]
    public void When_resolving_every_descriptor_exposed_by_migration_diagnostic_descriptors_then_each_one_has_a_resolvedescriptor_arm()
    {
        foreach (var descriptor in GetAllMigrationDiagnosticDescriptors())
        {
            var resolved = SolutionAnalysisRunner.ResolveDescriptor(descriptor.Id);

            resolved.Should().NotBeNull(
                $"'{descriptor.Id}' is exposed by {nameof(MigrationDiagnosticDescriptors)} and must resolve through " +
                $"{nameof(SolutionAnalysisRunner)}.{nameof(SolutionAnalysisRunner.ResolveDescriptor)} or it silently " +
                "disappears from every codemod report.");
            resolved!.Id.Should().Be(descriptor.Id);
        }
    }

    [Xunit.Fact]
    public void When_loading_migration_diagnostic_descriptors_then_every_id_is_unique()
    {
        var ids = GetAllMigrationDiagnosticDescriptors().Select(static descriptor => descriptor.Id).ToArray();

        ids.Should().OnlyHaveUniqueItems();
    }

    private static IReadOnlyList<DiagnosticDescriptor> GetAllMigrationDiagnosticDescriptors()
    {
        return typeof(MigrationDiagnosticDescriptors)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(static property => property.PropertyType == typeof(DiagnosticDescriptor))
            .Select(static property => (DiagnosticDescriptor)property.GetValue(null)!)
            .ToArray();
    }
}
