using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Modules.Observability;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Observability;

/// <summary>
/// Tests that <c>[DoNotRecordPerformanceMetrics]</c> suppresses Activity creation in the generated
/// pipeline by passing <c>suppressActivity: true</c> to the <c>OperationExecutorShell</c> base class.
/// </summary>
public class PerformanceMetricsModuleTests
{
    [Fact]
    public void When_operation_has_DoNotRecordPerformanceMetrics_then_pipeline_plan_sets_suppress_activity()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Auditing;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [DoNotRecordPerformanceMetrics]
            public partial class HealthCheckQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(HealthCheckQuery operation)
                {
                    return Task.FromResult("healthy");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new PerformanceMetricsModule(),
            new HandlerExecutionModule(),
            new AuditingModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.HealthCheckQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.SuppressActivity.Should().BeTrue(
            "[DoNotRecordPerformanceMetrics] should set SuppressActivity on the pipeline plan");
    }

    [Fact]
    public void When_normal_operation_then_pipeline_plan_does_not_suppress_activity()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class NormalQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(NormalQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new PerformanceMetricsModule(),
            new HandlerExecutionModule(),
            new AuditingModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.NormalQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.SuppressActivity.Should().BeFalse(
            "normal operations should not suppress Activity creation");
    }

    [Fact]
    public void When_DoNotRecordPerformanceMetrics_then_generated_pipeline_passes_suppress_activity_to_base()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Auditing;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [DoNotRecordPerformanceMetrics]
                public partial class HealthCheckQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(HealthCheckQuery operation)
                    {
                        return Task.FromResult("healthy");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated");

        var pipelineSource = pipelineSources
            .First(static s => s.HintName.Contains("HealthCheckQuery"))
            .SourceText;

        pipelineSource.Should().Contain("suppressActivity: true",
            "generated pipeline should pass suppressActivity: true to OperationExecutorShell for [DoNotRecordPerformanceMetrics] operations");
    }

    [Fact]
    public void When_normal_operation_then_generated_pipeline_does_not_pass_suppress_activity()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class NormalQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(NormalQuery operation)
                    {
                        return Task.FromResult("result");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = pipelineSources
            .First(static s => s.HintName.Contains("NormalQuery"))
            .SourceText;

        pipelineSource.Should().NotContain("suppressActivity",
            "normal operations should not pass suppressActivity to the base class");
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
