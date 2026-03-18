using System.Reflection;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTests
{
    public const string SnapshotsFolder = "Generators/OperationDescriptor/Snapshots/";

    [Fact]
    public void When_execution_plan_fails_then_propagates_module_diagnostics()
    {
        var method = typeof(OperationDescriptorSourceGenerator).GetMethod(
            "ConfigureDescriptorPlan",
            BindingFlags.NonPublic | BindingFlags.Static);
        var compilation = CreateCompilation();
        var failure = Result<BlueprintModuleExecutionPlan>.Failure(
            new EquatableList<DiagnosticInfo>(
            [
                new DiagnosticInfo(
                    new DiagnosticDescriptor(
                        "BPMOD003",
                        "Cyclic contributor dependency",
                        "The configured contributors for capability '{0}' contain a dependency cycle: {1}",
                        "BlueprintModuleSystem",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    (Location?)null,
                    new EquatableList<string>(["descriptor", "cycle"]))
            ]));

        method.Should().NotBeNull();

        var result = (Result<DescriptorPlan>)method!.Invoke(null, ["TestNamespace.IncludedOperation", compilation, failure])!;

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BPMOD003");
    }

    [Fact]
    public async Task When_not_partial_then_reports_diagnostic()
    {
        var source = """
                     public class NotPartialOperation : IOperation {}
                     """;

        await Compile(source)
            .VerifyCompilationFailedAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_executed_then_includes_marker_attributes()
    {
        var source = """
                     public partial class IncludedOperation : IOperation {}
                     """;

        await Compile(source)
            .VerifySuccessAsync(SnapshotsFolder, includeMarkerAttributes: true);
    }

    internal static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>("using Blueprint.Model;\nusing Blueprint.Model.Attributes;\n\nnamespace TestNamespace;\n\n" + source);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              using Blueprint.Model;

                              namespace TestNamespace;

                              public partial class IncludedOperation : IOperation
                              {
                              }
                              """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "OperationDescriptorSourceGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
