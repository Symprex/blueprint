using Blueprint.SourceGenerator.Modules.Observability;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Observability;

/// <summary>
/// Tests that <see cref="PerformanceMetricsModule"/> contributes <c>observability.activityEnabled</c> to the
/// per-operation debug manifest, reflecting whether the operation has opted out of Activity/trace creation.
/// </summary>
public class PerformanceMetricsManifestTests
{
    [Fact]
    public void When_operation_has_DoNotRecordPerformanceMetrics_then_manifest_entry_is_false()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Auditing;
            using Blueprint.Model;

            namespace TestNamespace;

            [DoNotRecordPerformanceMetrics]
            public partial class HealthCheckQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PerformanceMetricsModule()]);
        var result = config.BuildDebugManifest("TestNamespace.HealthCheckQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("observability.activityEnabled");
        result.Value.Entries["observability.activityEnabled"].ToString().Should().Be("False");
    }

    [Fact]
    public void When_normal_operation_then_manifest_entry_is_true()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class NormalQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new PerformanceMetricsModule()]);
        var result = config.BuildDebugManifest("TestNamespace.NormalQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("observability.activityEnabled");
        result.Value.Entries["observability.activityEnabled"].ToString().Should().Be("True");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
