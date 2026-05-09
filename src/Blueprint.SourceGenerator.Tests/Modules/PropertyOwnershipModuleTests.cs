using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Tests the compile-time property ownership analysis and descriptor/pipeline contributions
/// produced by <see cref="PropertyOwnershipModule"/>.
/// </summary>
public class PropertyOwnershipModuleTests
{
    private const string DescriptorSnapshotsFolder = "Generators/OperationDescriptor/Snapshots/";

    [Fact]
    public void When_post_command_has_route_and_body_properties_then_ownership_plan_has_route_and_body_entries()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/users/{Id}")]
            public partial class UpdateUserCommand : ICommand<string>
            {
                public string Id { get; set; }
                public string Name { get; set; }
                public string Email { get; set; }

                public static Task<string> HandleAsync(UpdateUserCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("UpdateUserCommandDescriptor") || s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull("descriptor should contain ownership feature data");
        descriptorSource!.Should().Contain("OperationPropertyOwnershipFeatureData");
        descriptorSource.Should().Contain("PropertyBindingSource.Route");
        descriptorSource.Should().Contain("PropertyBindingSource.Body");
    }

    [Fact]
    public void When_get_query_has_route_property_then_remaining_properties_are_query_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/users/{Id}")]
            public partial class GetUserQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(GetUserQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetUserQueryDescriptor") || s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();

        // GET query: remaining props should be Query-owned (not Body)
        descriptorSource!.Should().NotContain("PropertyBindingSource.Body");
        descriptorSource.Should().Contain("PropertyBindingSource.Query");
    }

    [Fact]
    public void When_route_property_has_unsupported_type_then_reports_BPPO001()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items/{Data}")]
            public partial class BadRouteTypeCommand : ICommand<string>
            {
                public System.Collections.Generic.List<int> Data { get; set; }

                public static Task<string> HandleAsync(BadRouteTypeCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id)
            .Should().Contain("BPPO001");
    }

    [Fact]
    public void When_get_command_has_explicit_http_get_and_no_route_params_then_properties_are_query_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.HttpGet]
            [global::Blueprint.Model.Attributes.RootLink("/search")]
            public partial class SearchCommand : ICommand<string>
            {
                public string Query { get; set; }

                public static Task<string> HandleAsync(SearchCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("SearchCommandDescriptor") || s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().NotContain("PropertyBindingSource.Body");
        descriptorSource.Should().Contain("PropertyBindingSource.Query");
    }

    [Fact]
    public void When_command_has_route_params_then_pipeline_has_population_stage_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/users/{Id}")]
            public partial class UpdateUserCommand : ICommand<string>
            {
                public string Id { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(UpdateUserCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.UpdateUserCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Population, nameof(PropertyOwnershipModule)));
        result.Value.RenderSteps.Should().Contain(static step =>
            step.Stage == PipelineStage.Population && step.ContributorId == nameof(PropertyOwnershipModule));
    }

    [Fact]
    public void When_command_has_body_properties_then_pipeline_requires_async()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/users/{Id}")]
            public partial class UpdateUserCommand : ICommand<string>
            {
                public string Id { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(UpdateUserCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.UpdateUserCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.RequiresAsync.Should().BeTrue("body reading requires async I/O");
    }

    [Fact]
    public void When_operation_is_not_http_exposed_then_module_does_not_contribute()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class NonHttpOperation : IQuery<string>
            {
                public string Name { get; set; }

                public static Task<string> HandleAsync(NonHttpOperation operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.NonHttpOperation", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(PropertyOwnershipModule));
    }

    [Fact]
    public void When_route_property_is_int_then_type_conversion_is_supported()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/users/{Id}")]
            public partial class GetUserByIdQuery : IQuery<string>
            {
                public int Id { get; set; }

                public static Task<string> HandleAsync(GetUserByIdQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        // int is a supported route type — should not report BPPO001
        result.Diagnostics.Where(static d => d.Id == "BPPO001").Should().BeEmpty();
    }

    [Fact]
    public void When_route_property_is_guid_then_type_conversion_is_supported()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/users/{Id}")]
            public partial class GetUserByGuidQuery : IQuery<string>
            {
                public System.Guid Id { get; set; }

                public static Task<string> HandleAsync(GetUserByGuidQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Where(static d => d.Id == "BPPO001").Should().BeEmpty();
    }

    [Fact]
    public async Task When_post_command_with_route_and_body_then_descriptor_snapshot_matches()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/users/{Id}")]
            public partial class UpdateUserCommand : ICommand<string>
            {
                public string Id { get; set; }
                public string Name { get; set; }
                public string Email { get; set; }

                public static Task<string> HandleAsync(UpdateUserCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        await result.VerifySuccessAsync(DescriptorSnapshotsFolder);
    }

    [Fact]
    public async Task When_post_command_with_route_and_body_then_pipeline_snapshot_matches()
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

                [RootLink("/users/{Id}")]
                [HttpPost]
                public partial class UpdateUserCommand : ICommand<string>
                {
                    public string Id { get; set; }
                    public string Name { get; set; }
                    public string Email { get; set; }

                    public static Task<string> HandleAsync(UpdateUserCommand operation)
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
            .UseDirectory("Generators/Pipelines/Snapshots/Integration/");
    }

    [Fact]
    public void When_get_query_has_query_properties_then_pipeline_has_population_stage()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/search")]
            public partial class SearchQuery : IQuery<string>
            {
                public string Term { get; set; }
                public int PageSize { get; set; }

                public static Task<string> HandleAsync(SearchQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.SearchQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Population, nameof(PropertyOwnershipModule)));
    }

    [Fact]
    public void When_get_query_has_typed_query_params_then_pipeline_requires_async()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/search")]
            public partial class SearchQuery : IQuery<string>
            {
                public string Term { get; set; }
                public int PageSize { get; set; }

                public static Task<string> HandleAsync(SearchQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.SearchQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.RequiresAsync.Should().BeTrue("typed query params need try/catch with early return");
    }

    [Fact]
    public void When_get_query_has_only_string_query_params_then_pipeline_does_not_require_async()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/search")]
            public partial class SearchQuery : IQuery<string>
            {
                public string Term { get; set; }
                public string Filter { get; set; }

                public static Task<string> HandleAsync(SearchQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.SearchQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.RequiresAsync.Should().BeFalse("string-only query params don't need try/catch with early return");
    }

    [Fact]
    public void When_query_property_has_unsupported_type_then_reports_BPPO003()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class SearchItemsQuery : IQuery<string>
            {
                public System.Collections.Generic.List<int> Data { get; set; }

                public static Task<string> HandleAsync(SearchItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id)
            .Should().Contain("BPPO003");
    }

    [Fact]
    public void When_delete_command_has_unowned_properties_then_they_are_query_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.HttpDelete]
            [global::Blueprint.Model.Attributes.RootLink("/items/{Id}")]
            public partial class DeleteItemCommand : ICommand<string>
            {
                public string Id { get; set; }
                public bool Force { get; set; }

                public static Task<string> HandleAsync(DeleteItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("DeleteItemCommandDescriptor") || s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Route");
        descriptorSource.Should().Contain("PropertyBindingSource.Query");
        descriptorSource.Should().NotContain("PropertyBindingSource.Body");
    }

    // ===== [FromX] attribute override tests =====

    [Fact]
    public void When_property_has_explicit_FromQuery_on_post_command_then_query_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromQuery(Name = "q")]
                public string SearchTerm { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Query");
        descriptorSource.Should().Contain("PropertyBindingSource.Body");

        // Verify name remapping: SearchTerm binds as "q" not "searchTerm"
        descriptorSource.Should().Contain("\"q\"");
    }

    [Fact]
    public void When_property_has_explicit_FromHeader_then_header_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromHeader("X-Tenant-Id")]
                public string TenantId { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Header");
        descriptorSource.Should().Contain("\"X-Tenant-Id\"");
    }

    [Fact]
    public void When_property_has_explicit_FromCookie_then_cookie_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromCookie("session_id")]
                public string SessionId { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Cookie");
        descriptorSource.Should().Contain("\"session_id\"");
    }

    [Fact]
    public void When_property_has_explicit_FromRoute_then_route_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/users/{Id}")]
            public partial class GetUserQuery : IQuery<string>
            {
                [global::Blueprint.Model.Attributes.FromRoute]
                public string Id { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(GetUserQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Route");
    }

    [Fact]
    public void When_property_has_explicit_FromBody_on_post_then_body_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromBody]
                public string Description { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Body");
    }

    [Fact]
    public void When_FromBody_on_get_operation_then_reports_BPPO004()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class SearchItemsQuery : IQuery<string>
            {
                [global::Blueprint.Model.Attributes.FromBody]
                public string Data { get; set; }

                public static Task<string> HandleAsync(SearchItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id)
            .Should().Contain("BPPO004");
    }

    [Fact]
    public void When_FromRoute_without_matching_placeholder_then_reports_BPPO005()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class GetItemQuery : IQuery<string>
            {
                [global::Blueprint.Model.Attributes.FromRoute]
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetItemQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id)
            .Should().Contain("BPPO005");
    }

    [Fact]
    public void When_property_has_conflicting_FromX_attributes_then_reports_BPPO006()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromQuery]
                [global::Blueprint.Model.Attributes.FromHeader("X-Custom")]
                public string Data { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id)
            .Should().Contain("BPPO006");
    }

    [Fact]
    public void When_FromHeader_on_unsupported_type_then_reports_BPPO007()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromHeader("X-Data")]
                public System.Collections.Generic.List<int> Data { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id)
            .Should().Contain("BPPO007");
    }

    [Fact]
    public void When_FromCookie_on_unsupported_type_then_reports_BPPO007()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromCookie("session")]
                public int[] Data { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Select(static d => d.Id)
            .Should().Contain("BPPO007");
    }

    [Fact]
    public void When_typed_header_property_then_pipeline_requires_async()
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
                [FromHeader("X-Page-Size")]
                public int PageSize { get; set; }

                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.RequiresAsync.Should().BeTrue("typed header values need TryParse with early return");
    }

    [Fact]
    public void When_string_header_and_cookie_only_then_pipeline_does_not_require_async()
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
                [FromHeader("X-Tenant")]
                public string TenantId { get; set; }
                [FromCookie("session")]
                public string SessionId { get; set; }

                public static Task<string> HandleAsync(GetItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.RequiresAsync.Should().BeFalse("string-only header/cookie doesn't need early return");
    }

    [Fact]
    public async Task When_post_command_with_mixed_explicit_and_convention_bindings_then_pipeline_snapshot_matches()
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

                /// <summary>
                /// POST command with explicit [FromQuery], [FromHeader], [FromCookie] alongside
                /// convention-based route and body bindings.
                /// </summary>
                [RootLink("/orgs/{OrgSlug}/items")]
                public partial class CreateItemCommand : ICommand<string>
                {
                    // Route: convention (placeholder match)
                    public string OrgSlug { get; set; }

                    // Query: explicit override on a POST command
                    [FromQuery(Name = "q")]
                    public string SearchFilter { get; set; }

                    // Header: explicit
                    [FromHeader("X-Tenant-Id")]
                    public string TenantId { get; set; }

                    // Header: explicit typed
                    [FromHeader("X-Request-Priority")]
                    public int Priority { get; set; }

                    // Cookie: explicit
                    [FromCookie("session_token")]
                    public string SessionToken { get; set; }

                    // Body: convention (remaining unowned on POST)
                    public string Name { get; set; }
                    public string Description { get; set; }

                    public static Task<string> HandleAsync(CreateItemCommand operation)
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

    [Fact]
    public async Task When_get_query_with_route_and_query_params_then_pipeline_snapshot_matches()
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

                [RootLink("/users/{Id}/items")]
                public partial class GetUserItemsQuery : IQuery<string>
                {
                    public string Id { get; set; }
                    public string Filter { get; set; }
                    public int PageSize { get; set; }
                    public int[] TagIds { get; set; }

                    public static Task<string> HandleAsync(GetUserItemsQuery operation)
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
            .UseDirectory("Generators/Pipelines/Snapshots/Integration/");
    }

    [Fact]
    public async Task When_query_only_get_then_pipeline_snapshot_matches()
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

                [RootLink("/search")]
                public partial class SearchAllQuery : IQuery<string>
                {
                    public string Term { get; set; }
                    public int? Page { get; set; }

                    public static Task<string> HandleAsync(SearchAllQuery operation)
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
        pipelineSources.Should().NotBeEmpty("a pipeline should be generated for query-only GET");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources)
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Generators/Pipelines/Snapshots/Integration/");
    }

    [Fact]
    public async Task When_operation_has_all_supported_conversion_types_then_pipeline_snapshot_matches()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                /// <summary>
                /// POST with route params of different types and body params of every supported type.
                /// Exercises route conversion (string, int, Guid) and body Utf8JsonReader paths
                /// (string, int, long, bool, decimal, Guid, DateTime).
                /// </summary>
                [RootLink("/orgs/{OrgSlug}/projects/{ProjectId}/items/{ItemGuid}")]
                [HttpPost]
                public partial class AllTypesPostCommand : ICommand<string>
                {
                    // Route: string (cast)
                    public string OrgSlug { get; set; }
                    // Route: int (Parse)
                    public int ProjectId { get; set; }
                    // Route: Guid (Parse)
                    public System.Guid ItemGuid { get; set; }

                    // Body: every scalar type
                    public string Title { get; set; }
                    public int Count { get; set; }
                    public long BigNumber { get; set; }
                    public double Rating { get; set; }
                    public float Score { get; set; }
                    public bool IsActive { get; set; }
                    public decimal Price { get; set; }
                    public System.Guid CorrelationId { get; set; }
                    public System.DateTime CreatedAt { get; set; }
                    public System.DateTimeOffset ModifiedAt { get; set; }

                    // Body: nullable value types (fall through to Deserialize<T?>)
                    public int? OptionalCount { get; set; }
                    public double? OptionalRating { get; set; }

                    public static Task<string> HandleAsync(AllTypesPostCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }

                /// <summary>
                /// GET with route params and query params covering every supported query type:
                /// scalar string, int, long, bool, decimal, Guid, DateTime, nullable int,
                /// string array, and int array.
                /// </summary>
                [RootLink("/orgs/{OrgSlug}/search/{CategoryId}")]
                public partial class AllTypesGetQuery : IQuery<string>
                {
                    // Route: string
                    public string OrgSlug { get; set; }
                    // Route: int
                    public int CategoryId { get; set; }

                    // Query: string scalar
                    public string Term { get; set; }
                    // Query: int scalar
                    public int PageSize { get; set; }
                    // Query: long scalar
                    public long Offset { get; set; }
                    // Query: double scalar
                    public double MinRating { get; set; }
                    // Query: bool scalar
                    public bool IncludeDeleted { get; set; }
                    // Query: decimal scalar
                    public decimal MinPrice { get; set; }
                    // Query: Guid scalar
                    public System.Guid TenantId { get; set; }
                    // Query: DateTime scalar
                    public System.DateTime Since { get; set; }
                    // Query: DateTimeOffset scalar
                    public System.DateTimeOffset ModifiedAfter { get; set; }
                    // Query: nullable int
                    public int? MaxResults { get; set; }
                    // Query: nullable double
                    public double? MaxPrice { get; set; }
                    // Query: string array
                    public string[] Tags { get; set; }
                    // Query: int array
                    public int[] CategoryIds { get; set; }

                    public static Task<string> HandleAsync(AllTypesGetQuery operation)
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
        pipelineSources.Should().HaveCountGreaterOrEqualTo(2, "pipelines for both operations should be generated");

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

    [Fact]
    public async Task When_operation_uses_init_only_properties_then_pipeline_snapshot_matches()
    {
        var source =
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/items/{Id}")]
                [HttpPost]
                public partial class UpdateInitCommand : ICommand<string>
                {
                    public required string Id { get; init; }
                    public required string Name { get; init; }
                    public int? Score { get; init; }

                    public static Task<string> HandleAsync(UpdateInitCommand operation)
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
            .UseDirectory("Generators/Pipelines/Snapshots/Integration/");
    }

    [Fact]
    public void When_root_link_uses_catch_all_route_parameter_then_descriptor_treats_property_as_route_owned()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/folders/{**FolderPath}")]
            public partial class GetFolderQuery : IQuery<string>
            {
                public string FolderPath { get; set; }

                public static Task<string> HandleAsync(GetFolderQuery operation)
                {
                    return Task.FromResult(operation.FolderPath);
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetFolderQueryDescriptor") || s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Route");
        descriptorSource.Should().Contain("FolderPath");
    }

    [Fact]
    public void When_self_link_uses_catch_all_route_parameter_then_descriptor_treats_property_as_route_owned()
    {
        var result = CompileDescriptor(
            """
            public partial class FolderApiResource : global::Blueprint.Model.ApiResource
            {
                public override string Object => "folder_api_resource";
                public string FolderPath { get; set; }
            }

            [global::Blueprint.Model.Attributes.SelfLink(typeof(FolderApiResource), "/folders/{**FolderPath}")]
            public partial class GetFolderResourceQuery : IQuery<string>
            {
                public string FolderPath { get; set; }

                public static Task<string> HandleAsync(GetFolderResourceQuery operation)
                {
                    return Task.FromResult(operation.FolderPath);
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetFolderResourceQueryDescriptor") || s.Contains("OperationPropertyOwnershipFeatureData"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("PropertyBindingSource.Route");
        descriptorSource.Should().Contain("FolderPath");
    }

    [Fact]
    public void When_root_link_uses_catch_all_route_parameter_then_pipeline_extracts_route_value()
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

                [RootLink("/folders/{**FolderPath}")]
                public partial class GetFolderPipelineQuery : IQuery<string>
                {
                    public string FolderPath { get; set; }

                    public static Task<string> HandleAsync(GetFolderPipelineQuery operation)
                    {
                        return Task.FromResult(operation.FolderPath);
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

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = string.Join(
            Environment.NewLine,
            result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator)).Select(static s => s.SourceText.ToString()));

        pipelineSource.Should().Contain("httpContext.Request.RouteValues[\"FolderPath\"]");
    }

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
        references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "PropertyOwnershipModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
