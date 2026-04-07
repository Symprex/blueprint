using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Tests the compile-time authentication analysis and descriptor/pipeline contributions
/// produced by <see cref="AuthenticationModule"/>.
/// </summary>
public class AuthenticationModuleTests
{
    // ── Descriptor tests ──────────────────────────────────────────────

    [Fact]
    public void When_operation_has_MustBeAuthenticated_and_http_exposure_then_descriptor_has_auth_feature_data()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items")]
            [MustBeAuthenticated]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("WellKnownFeatureData.RequiresAuthentication"));

        descriptorSource.Should().NotBeNull("descriptor should contain auth feature data");
    }

    [Fact]
    public void When_operation_has_no_auth_attribute_then_no_auth_feature_data()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/items")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetItemsQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().NotContain("WellKnownFeatureData.RequiresAuthentication");
    }

    [Fact]
    public void When_MustBeAuthenticated_on_non_http_operation_then_BPAU001_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [MustBeAuthenticated]
            public partial class ProcessItemCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(ProcessItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().Contain(static d => d.Id == "BPAU001");

        // Non-HTTP operations still get a descriptor, but it should not have auth feature data
        var allOutput = string.Join("\n", result.Output.Select(static t => t.ToString()));
        allOutput.Should().NotContain("WellKnownFeatureData.RequiresAuthentication");
    }

    // ── Pipeline plan tests ───────────────────────────────────────────

    [Fact]
    public void When_MustBeAuthenticated_and_http_exposed_then_pipeline_has_authentication_stage()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/items")]
            [MustBeAuthenticated]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Authentication, nameof(AuthenticationModule)));
    }

    [Fact]
    public void When_no_auth_attribute_then_no_authentication_stage()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/items")]
            public partial class GetItemsQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.Stage == PipelineStage.Authentication);
    }

    [Fact]
    public void When_MustBeAuthenticated_without_http_exposure_then_no_authentication_stage()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [MustBeAuthenticated]
            public partial class ProcessItemCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(ProcessItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new AuthenticationModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.Stage == PipelineStage.Authentication);
    }

    // ── Pipeline snapshot test ────────────────────────────────────────

    [Fact]
    public async Task When_MustBeAuthenticated_post_with_body_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/items/{Id}")]
                [MustBeAuthenticated]
                public partial class UpdateItemCommand : ICommand<string>
                {
                    public string Id { get; set; }
                    public string Name { get; set; }
                    public string Description { get; set; }

                    public static Task<string> HandleAsync(UpdateItemCommand operation)
                    {
                        return Task.FromResult("ok");
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
            .UseDirectory("Generators/Pipelines/Snapshots/Integration/");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static GenerationResult CompileDescriptor(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
            + "namespace TestNamespace;\n\n" + source);
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
            assemblyName: "AuthenticationModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
