using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Rebus;

/// <summary>
/// Tests for the <see cref="RebusTransactionModule"/> proving that the Rebus transaction scope
/// is correctly contributed to the generated pipeline and respects the NoRebusTransaction opt-out.
/// </summary>
public class RebusTransactionModuleTests
{
    [Fact]
    public void When_command_operation_then_plan_contains_rebus_setup_and_post_execution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class CreateOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(CreateOrderCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.CreateOrderCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var contributions = result.Value!.Contributions;

        contributions.Should().ContainInOrder(
            new PipelineContribution(PipelineStage.Setup, nameof(RebusTransactionModule)),
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)),
            new PipelineContribution(PipelineStage.PostExecution, nameof(RebusTransactionModule)));
    }

    [Fact]
    public void When_query_operation_then_plan_still_contains_rebus_transaction()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrderQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Setup, nameof(RebusTransactionModule)));
    }

    [Fact]
    public void When_NoRebusTransaction_attribute_then_plan_excludes_rebus_transaction()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Rebus;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [NoRebusTransaction]
            public partial class FireAndForgetCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(FireAndForgetCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.FireAndForgetCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(RebusTransactionModule),
            "NoRebusTransaction should exclude the module");
    }

    [Fact]
    public void When_composed_with_core_modules_then_rebus_complete_is_after_uow_and_outbox()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class PlaceOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(PlaceOrderCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new PropertyPopulationModule(),
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new MessageOutboxModule(),
            new AuditingModule(),
            new BackgroundTaskFlushModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.PlaceOrderCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var postExecutionContributions = result.Value!.Contributions
            .Where(static c => c.Stage == PipelineStage.PostExecution)
            .Select(static c => c.ContributorId)
            .ToList();

        // Rebus is a Consumer module (Category=1), core modules are Category=0.
        // The ordering invariant is: core PostExecution steps complete BEFORE
        // RebusTransactionScope.CompleteAsync(), ensuring data is persisted and
        // outbox is committed before Rebus messages are dispatched.
        postExecutionContributions.Should().ContainInOrder(
            nameof(UnitOfWorkModule),
            nameof(MessageOutboxModule),
            nameof(AuditingModule),
            nameof(BackgroundTaskFlushModule),
            nameof(RebusTransactionModule));
    }

    [Fact]
    public async Task When_composed_with_pipeline_generator_then_snapshot_includes_rebus_scope()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class PlaceOrderCommand : ICommand<string>
                {
                    public static Task<string> HandleAsync(PlaceOrderCommand operation)
                    {
                        return Task.FromResult("ordered");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<RebusCompanionPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"],
            [
                MetadataReference.CreateFromFile(typeof(global::Rebus.Transport.RebusTransactionScope).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Blueprint.Rebus.NoRebusTransactionAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Blueprint.Marten.MartenScopeAccessor).Assembly.Location),
            ]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(RebusCompanionPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated with Rebus module");

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
    public void When_rebus_not_referenced_then_module_does_not_contribute()
    {
        // Compilation WITHOUT Rebus assembly references — only Blueprint.Runtime.Model
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class SimpleCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(SimpleCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(static assembly => !assembly.GetName().Name!.StartsWith("Rebus", StringComparison.OrdinalIgnoreCase))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "NoRebusTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.SimpleCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(RebusTransactionModule),
            "RebusTransactionModule should auto-detect and skip when Rebus is not referenced");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Rebus.NoRebusTransactionAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::Rebus.Transport.RebusTransactionScope).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "RebusTransactionModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// A derived pipeline source generator that uses the default config (which now auto-detects Rebus).
/// The Rebus module activates automatically when <c>Rebus.Transport.RebusTransactionScope</c>
/// is found in the compilation, so no companion module composition is needed.
/// </summary>
internal sealed class RebusCompanionPipelineSourceGenerator : OperationExecutorPipelineSourceGenerator
{
    public RebusCompanionPipelineSourceGenerator()
        : base(CreateDefaultConfig())
    {
    }
}
