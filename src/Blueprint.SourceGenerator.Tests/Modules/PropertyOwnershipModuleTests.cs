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
    public void When_get_query_has_route_property_then_remaining_properties_are_not_body_owned()
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

        // GET query: remaining props should NOT be Body-owned (that's for POST/PUT/PATCH).
        // In Slice 1, GET remaining are left unowned (Query comes in Slice 2).
        descriptorSource!.Should().NotContain("PropertyBindingSource.Body");
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
    public void When_get_command_has_explicit_http_get_and_no_route_params_then_no_body_properties_generated()
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

        // GET operation: remaining properties should not be body-owned.
        // No BPPO002 warning in this slice — query binding comes in Slice 2.
        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("SearchCommandDescriptor") || s.Contains("OperationPropertyOwnershipFeatureData"));

        // Should not contain body binding for GET
        descriptorSource?.Should().NotContain("PropertyBindingSource.Body");
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
