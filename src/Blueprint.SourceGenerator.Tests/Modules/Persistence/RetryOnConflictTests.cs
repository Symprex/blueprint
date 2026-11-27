using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules.Persistence;

/// <summary>
/// Issue #200: <c>[RetryOnConflict]</c> reruns an operation whose unit of work save is refused, in a fresh scope. The
/// pipeline body becomes one attempt, run by a wrapper that reruns it; misuse is BPP003, BPP004 and BPP005.
/// </summary>
public class RetryOnConflictTests
{
    [Fact]
    public async Task When_operation_retries_on_conflict_then_its_pipeline_body_is_an_attempt_the_wrapper_reruns()
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

                [RetryOnConflict(2)]
                [ConcurrencyConflictError(nameof(ConcurrentUpdate))]
                public partial class EnsureReferenceCommand : ICommand
                {
                    public static readonly ApiExceptionFactory ConcurrentUpdate = new("Reference changed concurrently", "reference_concurrent_update", HttpStatusCode.Conflict);

                    public static Task HandleAsync(EnsureReferenceCommand operation) => Task.CompletedTask;
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

    [Fact]
    public void When_operation_does_not_retry_on_conflict_then_its_pipeline_has_no_attempt_method()
    {
        var result = Generate(Command(attributes: string.Empty));

        result.Diagnostics.Should().BeEmpty();
        PipelineSource(result).Should().NotContain("ExecuteAttemptAsync").And.NotContain("ExecuteRetryingRefusedSaveAsync");
    }

    [Fact]
    public void When_operation_retries_on_conflict_with_no_count_then_it_reruns_once()
    {
        var result = Generate(Command(attributes: "[RetryOnConflict]"));

        result.Diagnostics.Should().BeEmpty();
        PipelineSource(result).Should().Contain("ExecuteRetryingRefusedSaveAsync(this, context, 1, ");
    }

    [Fact]
    public void When_operation_that_does_not_save_retries_on_conflict_then_BPP003()
    {
        var result = Generate(Command(attributes: "[RetryOnConflict][DoNotSaveChanges]"));

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPP003")
            .Which.GetMessage().Should().Contain("EnsureReferenceCommand");
    }

    [Fact]
    public void When_operation_with_no_rebus_transaction_retries_on_conflict_then_BPP004()
    {
        // Without the pipeline's own Rebus transaction, a send joins the host's and survives the refused attempt (ADR 0001).
        var source = Command(attributes: "[RetryOnConflict][Blueprint.Rebus.NoRebusTransaction]") + """

            namespace Blueprint.Rebus
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class NoRebusTransactionAttribute : System.Attribute { }
            }
            """;

        var result = Generate(source);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPP004")
            .Which.GetMessage().Should().Contain("EnsureReferenceCommand");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void When_operation_retries_on_conflict_a_count_outside_one_to_five_times_then_BPP005(int retries)
    {
        var result = Generate(Command(attributes: $"[RetryOnConflict({retries})]"));

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPP005")
            .Which.GetMessage().Should().Contain(retries.ToString(System.Globalization.CultureInfo.InvariantCulture)).And.Contain("EnsureReferenceCommand");
    }

    private static string Command(string attributes)
    {
        return $$"""
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Persistence;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                {{attributes}}
                public partial class EnsureReferenceCommand : ICommand
                {
                    public static Task HandleAsync(EnsureReferenceCommand operation) => Task.CompletedTask;
                }
            }
            """;
    }

    private static string PipelineSource(MultiGeneratorGenerationResult result)
    {
        return result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator))
            .Single(static s => s.HintName.EndsWith("EnsureReferenceCommandExecutorPipeline.g.cs", StringComparison.Ordinal))
            .SourceText;
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
