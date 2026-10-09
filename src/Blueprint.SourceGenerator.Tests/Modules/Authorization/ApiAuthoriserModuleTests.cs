using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules.Authorization;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Authorization;

/// <summary>
/// Tests which operations <see cref="ApiAuthoriserModule"/> gives a user context load and an authoriser invocation: every
/// one, linked or not, as legacy Blueprint's <c>UserContextLoaderMiddlewareBuilder</c> and
/// <c>AuthorisationMiddlewareBuilder</c> matched every operation, with only an <c>[AllowAnonymous]</c> one left without the
/// invocation.
/// </summary>
public class ApiAuthoriserModuleTests
{
    [Fact]
    public void When_operation_has_no_link_then_pipeline_loads_the_user_context_and_invokes_the_authorisers()
    {
        var result = BuildPlan(
            """
            public partial class ProcessItemCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(ProcessItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """,
            "TestNamespace.ProcessItemCommand");

        result.Contributions.Should().Contain(new PipelineContribution(PipelineStage.Authentication, nameof(ApiAuthoriserModule)));
        result.Contributions.Should().Contain(new PipelineContribution(PipelineStage.Authorisation, nameof(ApiAuthoriserModule)));
    }

    [Fact]
    public void When_operation_has_no_link_and_allows_anonymous_access_then_pipeline_loads_the_user_context_only()
    {
        var result = BuildPlan(
            """
            [AllowAnonymous]
            public partial class ProcessItemCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(ProcessItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """,
            "TestNamespace.ProcessItemCommand");

        result.Contributions.Should().Contain(new PipelineContribution(PipelineStage.Authentication, nameof(ApiAuthoriserModule)));
        result.Contributions.Should().NotContain(static c => c.Stage == PipelineStage.Authorisation);
    }

    [Fact]
    public void When_operation_has_a_link_then_pipeline_loads_the_user_context_and_invokes_the_authorisers()
    {
        var result = BuildPlan(
            """
            [RootLink("/items")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """,
            "TestNamespace.GetItemsQuery");

        result.Contributions.Should().Contain(new PipelineContribution(PipelineStage.Authentication, nameof(ApiAuthoriserModule)));
        result.Contributions.Should().Contain(new PipelineContribution(PipelineStage.Authorisation, nameof(ApiAuthoriserModule)));
    }

    [Fact]
    public async Task When_operation_has_no_link_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class ProcessItemCommand : ICommand<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(ProcessItemCommand operation)
                    {
                        return Task.FromResult(operation.Id);
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

    private static PipelinePlan BuildPlan(string source, string operationTypeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\nnamespace TestNamespace;\n\n" + source,
            new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: nameof(ApiAuthoriserModuleTests),
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var config = new BlueprintSourceGenerationConfig([new ApiAuthoriserModule()]);
        var result = config.BuildPipelinePlan(operationTypeName, compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        return result.Value!;
    }
}
