using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>PerformanceMetricsModule</c> correctly detects <c>[DoNotRecordPerformanceMetrics]</c>
/// and sets the <c>SuppressActivity</c> flag in the pipeline plan.
/// </summary>
public class PerformanceMetricsModuleTests
{
    [Fact]
    public void When_operation_has_do_not_record_attribute_then_activity_is_suppressed()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Auditing;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [DoNotRecordPerformanceMetrics]
                public partial class SilentCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(SilentCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // When [DoNotRecordPerformanceMetrics] is applied, the activity is suppressed via base constructor arg
        pipelineSource.Should().Contain("suppressActivity: true");
    }

    [Fact]
    public void When_operation_has_no_suppress_attribute_then_activity_is_not_suppressed()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class NormalCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(NormalCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Normal operations should NOT suppress activity
        pipelineSource.Should().NotContain("suppressActivity: true");
    }
}
