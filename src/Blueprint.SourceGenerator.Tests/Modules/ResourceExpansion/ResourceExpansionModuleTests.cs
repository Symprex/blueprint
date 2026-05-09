using System.Text;

using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.ResourceExpansion;

/// <summary>
/// Tests that <see cref="ResourceExpansionModule"/> correctly discovers <c>Expandable&lt;T&gt;</c>
/// properties at compile time and generates expansion pipeline code for both client-requested
/// and always-expand properties.
/// </summary>
public class ResourceExpansionModuleTests
{
    // ── Pipeline plan tests ───────────────────────────────────────────

    [Fact]
    public void When_response_has_expandable_property_then_pipeline_has_post_execution_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public class ChildResource : ApiResource { }

            public class MyResource : ApiResource
            {
                public Expandable<ChildResource> Child { get; set; }
            }

            public partial class GetMyResourceQuery : IQuery<MyResource>
            {
                public static Task<MyResource> HandleAsync(GetMyResourceQuery operation)
                {
                    return Task.FromResult(new MyResource());
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new ResourceExpansionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetMyResourceQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(ResourceExpansionModule)));
    }

    [Fact]
    public void When_response_has_no_expandable_properties_then_no_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public class PlainResource : ApiResource
            {
                public string Name { get; set; }
            }

            public partial class GetPlainQuery : IQuery<PlainResource>
            {
                public static Task<PlainResource> HandleAsync(GetPlainQuery operation)
                {
                    return Task.FromResult(new PlainResource());
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new ResourceExpansionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetPlainQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(ResourceExpansionModule));
    }

    [Fact]
    public void When_operation_has_no_response_type_then_no_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class FireAndForgetCommand : ICommand
            {
                public static Task HandleAsync(FireAndForgetCommand operation)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new ResourceExpansionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.FireAndForgetCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(ResourceExpansionModule));
    }

    [Fact]
    public void When_response_has_collection_of_expandable_then_pipeline_has_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public class TagResource : ApiResource { }

            public class ItemResource : ApiResource
            {
                public List<Expandable<TagResource>> Tags { get; set; }
            }

            public partial class GetItemQuery : IQuery<ItemResource>
            {
                public static Task<ItemResource> HandleAsync(GetItemQuery operation)
                {
                    return Task.FromResult(new ItemResource());
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new ResourceExpansionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(ResourceExpansionModule)));
    }

    // ── Pipeline snapshot tests ───────────────────────────────────────

    [Fact]
    public async Task When_expandable_property_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public class ChildResource : ApiResource
                {
                    public override string Object => "child";
                }

                public class ParentResource : ApiResource
                {
                    public override string Object => "parent";
                    public Expandable<ChildResource> Child { get; set; }
                }

                public partial class GetParentQuery : IQuery<ParentResource>
                {
                    public static Task<ParentResource> HandleAsync(GetParentQuery operation)
                    {
                        return Task.FromResult(new ParentResource());
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
    public async Task When_always_expand_property_then_pipeline_snapshot_matches()
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

                public class DetailResource : ApiResource
                {
                    public override string Object => "detail";
                }

                public class SummaryResource : ApiResource
                {
                    public override string Object => "summary";
                    [AlwaysExpand]
                    public Expandable<DetailResource> Detail { get; set; }
                }

                public partial class GetSummaryQuery : IQuery<SummaryResource>
                {
                    public static Task<SummaryResource> HandleAsync(GetSummaryQuery operation)
                    {
                        return Task.FromResult(new SummaryResource());
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
    public async Task When_nested_expandable_property_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public class GroupResource : ApiResource
                {
                    public override string Object => "group";
                }

                public class SubItem
                {
                    public Expandable<GroupResource> Group { get; set; }
                }

                public class TopResource : ApiResource
                {
                    public override string Object => "top";
                    public SubItem Details { get; set; }
                    public List<Expandable<GroupResource>> Groups { get; set; }
                }

                public partial class GetTopQuery : IQuery<TopResource>
                {
                    public static Task<TopResource> HandleAsync(GetTopQuery operation)
                    {
                        return Task.FromResult(new TopResource());
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

    // ── Helper ────────────────────────────────────────────────────────

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "ResourceExpansionModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
