using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules.Persistence;

/// <summary>
/// Issue #197: <c>[SaveRefusedError]</c> names the factory whose 409 answers a refused unit of work save. The pipeline
/// passes it to the save; a name that is not a static <c>ApiExceptionFactory</c> of the operation is BPP002.
/// </summary>
public class SaveRefusedErrorTests
{
    [Fact]
    public async Task When_operation_names_its_save_refused_error_then_the_save_is_passed_that_factory()
    {
        const string source = """
            using System.Net;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Persistence;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [SaveRefusedError(nameof(ConcurrentUpdate))]
                public partial class SetReferenceCommand : ICommand
                {
                    public static readonly ApiExceptionFactory ConcurrentUpdate = new("Reference changed concurrently", "reference_concurrent_update", HttpStatusCode.Conflict);

                    public static Task HandleAsync(SetReferenceCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var result = Generate(source);

        result.Diagnostics.Should().BeEmpty();

        var outputText = new StringBuilder();
        foreach (var s in result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator)).OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots");
    }

    [Theory]
    [InlineData("""public static readonly ApiExceptionFactory ConcurrentUpdate = new("t", "t", 409);""", "Missing")]
    [InlineData("""public readonly ApiExceptionFactory ConcurrentUpdate = new("t", "t", 409);""", "ConcurrentUpdate")]
    [InlineData("""public static readonly string ConcurrentUpdate = "t";""", "ConcurrentUpdate")]

    // The generated pipeline is a class of its own, so it cannot read a private member or a private getter (PR #198 review).
    [InlineData("""private static readonly ApiExceptionFactory ConcurrentUpdate = new("t", "t", 409);""", "ConcurrentUpdate")]
    [InlineData("""public static ApiExceptionFactory ConcurrentUpdate { private get; set; } = new("t", "t", 409);""", "ConcurrentUpdate")]
    public void When_operation_names_no_static_factory_of_its_own_then_BPP002(string member, string name)
    {
        var source = $$"""
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Persistence;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [SaveRefusedError("{{name}}")]
                public partial class SetReferenceCommand : ICommand
                {
                    {{member}}

                    public static Task HandleAsync(SetReferenceCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var result = Generate(source);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPP002")
            .Which.GetMessage().Should().Contain($"'{name}'").And.Contain("SetReferenceCommand");
    }

    [Theory]
    [InlineData("""internal static readonly ApiExceptionFactory @event = new("t", "t", 409);""", "global::TestNamespace.SetReferenceCommand.@event")]
    [InlineData("""internal static ApiExceptionFactory Taken => new("t", "t", 409);""", "global::TestNamespace.SetReferenceCommand.Taken")]
    public void When_operation_names_a_factory_the_pipeline_can_read_then_the_save_is_passed_it_and_compiles(string member, string expected)
    {
        // A keyword-named member used to be emitted unescaped (`.event`), which does not compile (PR #198 review).
        var name = member.Contains("@event", StringComparison.Ordinal) ? "event" : "Taken";
        var source = $$"""
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Persistence;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [SaveRefusedError("{{name}}")]
                public partial class SetReferenceCommand : ICommand
                {
                    {{member}}

                    public static Task HandleAsync(SetReferenceCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var result = Generate(source);

        result.Diagnostics.Should().BeEmpty();
        result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator))
            .Single(static s => s.HintName.EndsWith("SetReferenceCommandExecutorPipeline.g.cs", StringComparison.Ordinal))
            .SourceText.Should().Contain($"await SaveUnitOfWorkAsync(context.ServiceProvider, context.OperationCancelled, {expected}).ConfigureAwait(false);");
    }

    [Fact]
    public void When_operation_names_no_save_refused_error_then_the_save_takes_no_factory()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class SetReferenceCommand : ICommand
                {
                    public static Task HandleAsync(SetReferenceCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var result = Generate(source);

        result.Diagnostics.Should().BeEmpty();
        result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator))
            .Single(static s => s.HintName.EndsWith("SetReferenceCommandExecutorPipeline.g.cs", StringComparison.Ordinal))
            .SourceText.Should().Contain("await SaveUnitOfWorkAsync(context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);");
    }

    private static MultiGeneratorGenerationResult Generate(string source)
    {
        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        return TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);
    }
}
