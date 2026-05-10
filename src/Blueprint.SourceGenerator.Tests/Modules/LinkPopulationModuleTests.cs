using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

public class LinkPopulationModuleTests
{
    [Fact]
    public async Task When_operation_returns_resource_event_result_with_self_link_then_pipeline_populates_links()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class TestApiResource : ApiResource
                {
                    public override string Object => "test_api_resource";
                    public string Id { get; set; }
                    public string Name { get; set; }
                }

                [SelfLink(typeof(TestApiResource), "/resources/{Id}")]
                public partial class CreateResourceCommand : ICommand<ResourceEventResult<TestApiResource>>
                {
                    public string Id { get; set; }
                    public string Name { get; set; }

                    public static Task<ResourceEventResult<TestApiResource>> HandleAsync(CreateResourceCommand operation)
                    {
                        var resource = new TestApiResource { Id = operation.Id, Name = operation.Name };
                        return Task.FromResult(new ResourceEventResult<TestApiResource>(ChangeType.Created, "created", operation.Id, resource));
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
        pipelineSources.Should().NotBeEmpty("a pipeline should be generated");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources)
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Generators/Pipelines/Snapshots/LinkPopulation/");
    }

    [Fact]
    public async Task When_operation_returns_plain_value_with_cross_operation_self_link_then_pipeline_populates_links()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class WidgetApiResource : ApiResource
                {
                    public override string Object => "widget_api_resource";
                    public string Id { get; set; }
                    public string Label { get; set; }
                }

                [SelfLink(typeof(WidgetApiResource), "/widgets/{Id}")]
                public partial class UpdateWidgetCommand : ICommand<ResourceEventResult<WidgetApiResource>>
                {
                    public string Id { get; set; }
                    public string Label { get; set; }

                    public static Task<ResourceEventResult<WidgetApiResource>> HandleAsync(UpdateWidgetCommand operation)
                    {
                        var resource = new WidgetApiResource { Id = operation.Id, Label = operation.Label };
                        return Task.FromResult(new ResourceEventResult<WidgetApiResource>(ChangeType.Updated, "updated", operation.Id, resource));
                    }
                }

                [RootLink("/widgets/{Id}")]
                public partial class GetWidgetQuery : IQuery<WidgetApiResource>
                {
                    public string Id { get; set; }

                    public static Task<WidgetApiResource> HandleAsync(GetWidgetQuery operation)
                    {
                        return Task.FromResult(new WidgetApiResource { Id = operation.Id, Label = "Widget" });
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
        pipelineSources.Should().NotBeEmpty("pipelines should be generated");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources)
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Generators/Pipelines/Snapshots/LinkPopulation/");
    }

    [Fact]
    public void When_self_link_uses_catch_all_route_parameter_then_descriptor_compilation_has_no_diagnostics()
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

                public partial class FolderApiResource : ApiResource
                {
                    public override string Object => "folder_api_resource";
                    public string FolderPath { get; set; }
                }

                [SelfLink(typeof(FolderApiResource), "/folders/{**FolderPath}")]
                public partial class GetFolderQuery : IQuery<FolderApiResource>
                {
                    public string FolderPath { get; set; }

                    public static Task<FolderApiResource> HandleAsync(GetFolderQuery operation)
                    {
                        return Task.FromResult(new FolderApiResource { FolderPath = operation.FolderPath });
                    }
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString()).Single(static s => s.Contains("GetFolderQueryDescriptor"));
        descriptorSource.Should().Contain("/folders/{**FolderPath}");
    }

    [Fact]
    public void When_self_link_uses_catch_all_route_parameter_then_compiled_url_builder_preserves_path_separators()
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

                public partial class FolderApiResource : ApiResource
                {
                    public override string Object => "folder_api_resource";
                    public string FolderPath { get; set; }
                }

                [SelfLink(typeof(FolderApiResource), "/folders/{**FolderPath}")]
                public partial class GetFolderQuery : IQuery<FolderApiResource>
                {
                    public string FolderPath { get; set; }

                    public static Task<FolderApiResource> HandleAsync(GetFolderQuery operation)
                    {
                        return Task.FromResult(new FolderApiResource { FolderPath = operation.FolderPath });
                    }
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString()).Single(static s => s.Contains("GetFolderQueryDescriptor"));
        descriptorSource.Should().Contain("$\"/folders/{r.FolderPath?.ToString() ?? \"\"}\"");
        descriptorSource.Should().NotContain("EscapeDataString(r.FolderPath");
    }

    [Fact]
    public void When_multiple_distinct_self_link_urls_target_same_resource_then_BPLG004_is_emitted()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class SharedResource : ApiResource
                {
                    public override string Object => "shared_resource";
                    public string Id { get; set; }
                    public string Slug { get; set; }
                }

                [SelfLink(typeof(SharedResource), "/resources/{Id}")]
                public partial class GetResourceByIdQuery : IQuery<SharedResource>
                {
                    public string Id { get; set; }
                    public static Task<SharedResource> HandleAsync(GetResourceByIdQuery op) => throw new System.NotImplementedException();
                }

                [SelfLink(typeof(SharedResource), "/resources/by-slug/{Slug}")]
                public partial class GetResourceBySlugQuery : IQuery<SharedResource>
                {
                    public string Slug { get; set; }
                    public static Task<SharedResource> HandleAsync(GetResourceBySlugQuery op) => throw new System.NotImplementedException();
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

        result.Diagnostics.Should().Contain(
            d => d.Descriptor.Id == "BPLG004",
            "ambiguous self-link warning should be emitted when multiple URLs target same resource type");
    }

    [Fact]
    public void When_RootLink_has_empty_url_then_BPLG005_is_emitted()
    {
        const string source = """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [RootLink("")]
                public partial class BadQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(BadQuery op) => throw new System.NotImplementedException();
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPLG005");
    }

    [Fact]
    public void When_SelfLink_has_empty_url_then_BPLG005_is_emitted()
    {
        const string source = """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                public partial class TestResource : ApiResource
                {
                    public override string Object => "test_resource";
                    public string Id { get; set; }
                }

                [SelfLink(typeof(TestResource), "")]
                public partial class BadCommand : ICommand<ResourceEventResult<TestResource>>
                {
                    public string Id { get; set; }
                    public static Task<ResourceEventResult<TestResource>> HandleAsync(BadCommand op) => throw new System.NotImplementedException();
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPLG005");
    }

    [Fact]
    public void When_RootLink_has_whitespace_url_then_BPLG005_is_emitted()
    {
        const string source = """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [RootLink("   ")]
                public partial class BadQuery : IQuery<string>
                {
                    public static Task<string> HandleAsync(BadQuery op) => throw new System.NotImplementedException();
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPLG005");
    }
}
