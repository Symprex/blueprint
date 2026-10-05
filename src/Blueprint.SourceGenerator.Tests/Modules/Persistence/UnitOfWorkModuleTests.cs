using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Persistence;

/// <summary>
/// Tests that <see cref="UnitOfWorkModule"/> correctly generates persistence coordination
/// code for command and background-task pipelines and does not contribute to query pipelines.
/// </summary>
public class UnitOfWorkModuleTests
{
    // ── Pipeline plan tests ───────────────────────────────────────────

    [Fact]
    public void When_command_operation_then_pipeline_has_post_execution_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class SaveOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(SaveOrderCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.SaveOrderCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(UnitOfWorkModule)));
    }

    [Fact]
    public void When_query_operation_then_pipeline_has_post_execution_contribution()
    {
        // R138: legacy's SaveMartenChangesMiddleware matched any operation without [DoNotSaveChanges] (Signature365
        // 8b40ecc961, Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29), queries included: Portal's
        // Microsoft365AddOnConnectQueryExecutorPipeline.cs:133-134 saves the session after the handler.
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetOrdersQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrdersQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrdersQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(UnitOfWorkModule)));
    }

    [Fact]
    public void When_background_task_then_pipeline_has_unit_of_work_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class ProcessOrderTask : IBackgroundTask
            {
                public static Task HandleAsync(ProcessOrderTask operation)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessOrderTask", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(UnitOfWorkModule)));
    }

    [Fact]
    public void When_command_has_DoNotSaveChanges_then_pipeline_has_no_unit_of_work_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Persistence;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [DoNotSaveChanges]
            public partial class DryRunCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(DryRunCommand operation)
                {
                    return Task.FromResult("preview-only");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.DryRunCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(UnitOfWorkModule));
    }

    [Fact]
    public void When_query_has_DoNotSaveChanges_then_pipeline_has_no_unit_of_work_contribution_and_no_diagnostic()
    {
        // R138: the opt-out has effect on a query now, as legacy's did (Signature365 8b40ecc961, Outlook
        // GetAddInSignaturesQuery.cs:35 carried [DoNotSaveChanges]), so BPP001 ("no effect on queries") is retired.
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Persistence;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [DoNotSaveChanges]
            public partial class GetOrdersQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrdersQuery operation)
                {
                    return Task.FromResult("result");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrdersQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(UnitOfWorkModule));
    }

    [Fact]
    public void When_background_task_has_DoNotSaveChanges_then_pipeline_has_no_unit_of_work_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Persistence;
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [DoNotSaveChanges]
            public partial class ProcessOrderTask : IBackgroundTask
            {
                public static Task HandleAsync(ProcessOrderTask operation)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessOrderTask", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(UnitOfWorkModule));
    }

    // ── Pipeline snapshot test ────────────────────────────────────────

    [Fact]
    public async Task When_command_with_handler_then_pipeline_snapshot_includes_unit_of_work_save()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class SaveOrderCommand : ICommand<string>
                {
                    public string OrderId { get; set; }

                    public static Task<string> HandleAsync(SaveOrderCommand operation)
                    {
                        return Task.FromResult("saved");
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

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots");
    }

    [Fact]
    public async Task When_query_then_pipeline_snapshot_includes_unit_of_work_save_unless_nested()
    {
        // R138: a query saves as a command does, except in a nested execution (a resource event's self query), which
        // legacy ran through a pipeline without the save (SaveMartenChangesMiddleware.SupportsNestedExecution => false,
        // CodeGennedExecutor.cs:99-100 at Signature365 8b40ecc961).
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class GetOrdersQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(GetOrdersQuery operation)
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
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots");
    }

    [Fact]
    public void When_query_saves_then_the_save_is_skipped_in_a_nested_execution_and_a_commands_is_not()
    {
        // R138: legacy left the save out of every nested pipeline (SupportsNestedExecution => false). A query's is new, so it
        // is skipped when nested, as a self query's was; a command's and a background task's save runs as it did before,
        // nested or not.
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class GetOrdersQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(GetOrdersQuery operation) => Task.FromResult("result");
                }

                public partial class SaveOrderCommand : ICommand<string>
                {
                    public static Task<string> HandleAsync(SaveOrderCommand operation) => Task.FromResult("saved");
                }

                public partial class ProcessOrderTask : IBackgroundTask
                {
                    public static Task HandleAsync(ProcessOrderTask operation) => Task.CompletedTask;
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

        var pipelines = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));

        SaveGuardOf(pipelines, "GetOrdersQueryExecutorPipeline").Should().Be("if (this.HasUnitOfWork && !context.IsNested)");
        SaveGuardOf(pipelines, "SaveOrderCommandExecutorPipeline").Should().Be("if (this.HasUnitOfWork)");
        SaveGuardOf(pipelines, "ProcessOrderTaskExecutorPipeline").Should().Be("if (this.HasUnitOfWork)");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static string SaveGuardOf(IEnumerable<GeneratedSourceSnapshot> pipelines, string pipelineName)
    {
        var lines = pipelines.Single(s => s.HintName.EndsWith(pipelineName + ".g.cs", StringComparison.Ordinal))
            .SourceText.Split('\n')
            .Select(static line => line.Trim())
            .ToList();
        var saveLine = lines.FindIndex(static line => line.StartsWith("await SaveUnitOfWorkAsync(", StringComparison.Ordinal));

        saveLine.Should().BeGreaterThan(1, "the pipeline must save its unit of work");

        return lines[saveLine - 2];
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "UnitOfWorkModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
