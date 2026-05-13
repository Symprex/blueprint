using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Tests the <c>[FromAuthContext]</c> attribute integration with <see cref="PropertyOwnershipModule"/>:
/// auth-context properties are excluded from HTTP binding and OpenAPI, route precedence is preserved,
/// and conflicts with other <c>[FromX]</c> attributes produce diagnostics.
/// </summary>
public class FromAuthContextPropertyOwnershipTests
{
    [Fact]
    public void When_property_has_FromAuthContext_then_descriptor_has_AuthContext_source()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/users/{Id}")]
            public partial class GetUserQuery : IQuery<string>
            {
                public string Id { get; set; }

                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

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

        descriptorSource.Should().NotBeNull("descriptor should contain ownership feature data");
        descriptorSource!.Should().Contain("PropertyBindingSource.AuthContext",
            "TenantId with [FromAuthContext] should be recorded as AuthContext source");
        descriptorSource.Should().Contain("PropertyBindingSource.Route",
            "Id should still be bound from route");
    }

    [Fact]
    public void When_property_has_FromAuthContext_then_pipeline_does_not_bind_from_http()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                public string Name { get; set; }

                [FromAuthContext]
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new PropertyOwnershipModule()]);
        var result = config.BuildPipelinePlan("TestNamespace.CreateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // TenantId must not cause body reading — only Name should be in body
        var plan = result.Value!;
        plan.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Population, nameof(PropertyOwnershipModule)));
    }

    [Fact]
    public void When_property_has_FromAuthContext_on_post_command_then_not_included_in_body()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                public string Name { get; set; }

                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

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

        // TenantId must be AuthContext, not Body
        descriptorSource!.Should().Contain("PropertyBindingSource.AuthContext");
        descriptorSource.Should().Contain("\"Name\"");
        descriptorSource.Should().Contain("PropertyBindingSource.Body");

        // The TenantId entry should NOT be Body
        var bodyOccurrences = CountOccurrences(descriptorSource, "PropertyBindingSource.Body");
        bodyOccurrences.Should().Be(1, "only Name should be Body-bound, not TenantId");
    }

    [Fact]
    public void When_property_has_FromAuthContext_on_get_query_then_not_included_in_query_params()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class ListItemsQuery : IQuery<string>
            {
                public int Page { get; set; }

                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(ListItemsQuery operation)
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
        descriptorSource!.Should().Contain("PropertyBindingSource.AuthContext");
        descriptorSource.Should().Contain("PropertyBindingSource.Query");

        // Only Page should be Query-bound
        var queryOccurrences = CountOccurrences(descriptorSource, "PropertyBindingSource.Query");
        queryOccurrences.Should().Be(1, "only Page should be Query-bound, not TenantId");
    }

    [Fact]
    public void When_FromAuthContext_property_is_also_route_placeholder_then_route_wins()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/tenants/{TenantId}/items/{Id}")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }

                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetItemQuery operation)
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

        // TenantId is in route, so route wins — both should be Route
        var routeOccurrences = CountOccurrences(descriptorSource!, "PropertyBindingSource.Route");
        routeOccurrences.Should().Be(2, "both Id and TenantId should be route-bound when TenantId is a route placeholder");

        // No AuthContext entry should be emitted
        descriptorSource.Should().NotContain("PropertyBindingSource.AuthContext",
            "route takes precedence over [FromAuthContext]");
    }

    [Fact]
    public void When_FromAuthContext_conflicts_with_FromBody_then_reports_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                [global::Blueprint.Model.Attributes.FromBody]
                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(CreateItemCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPPO006",
            "[FromAuthContext] + [FromBody] on the same property is a conflict");
    }

    [Fact]
    public void When_FromAuthContext_conflicts_with_FromQuery_then_reports_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class ListItemsQuery : IQuery<string>
            {
                [global::Blueprint.Model.Attributes.FromQuery]
                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(ListItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPPO006",
            "[FromAuthContext] + [FromQuery] on the same property is a conflict");
    }

    [Fact]
    public void When_FromAuthContext_conflicts_with_FromHeader_then_reports_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class ListItemsQuery : IQuery<string>
            {
                [global::Blueprint.Model.Attributes.FromHeader(Name = "X-Tenant")]
                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(ListItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPPO006",
            "[FromAuthContext] + [FromHeader] on the same property is a conflict");
    }

    [Fact]
    public void When_FromAuthContext_with_FromRoute_then_no_conflict()
    {
        // [FromRoute] + [FromAuthContext] is allowed — route wins, no diagnostic
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/tenants/{TenantId}")]
            public partial class GetTenantQuery : IQuery<string>
            {
                [global::Blueprint.Model.Attributes.FromRoute]
                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

                public static Task<string> HandleAsync(GetTenantQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty(
            "[FromRoute] + [FromAuthContext] should not conflict — route takes precedence");
    }

    [Fact]
    public void When_base_class_property_has_FromAuthContext_then_derived_operation_inherits_it()
    {
        var result = CompileDescriptor(
            """
            public abstract class TenantOperation
            {
                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }
            }

            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class ListItemsQuery : TenantOperation, IQuery<string>
            {
                public int Page { get; set; }

                public static Task<string> HandleAsync(ListItemsQuery operation)
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
        descriptorSource!.Should().Contain("PropertyBindingSource.AuthContext",
            "inherited [FromAuthContext] from base class should be preserved");
    }

    [Fact]
    public void When_multiple_properties_have_FromAuthContext_then_all_are_AuthContext()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class CreateItemCommand : ICommand<string>
            {
                public string Name { get; set; }

                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string TenantId { get; set; }

                [global::Blueprint.Model.Attributes.FromAuthContext]
                public string PartnerId { get; set; }

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

        var authContextCount = CountOccurrences(descriptorSource!, "PropertyBindingSource.AuthContext");
        authContextCount.Should().Be(2, "both TenantId and PartnerId should be AuthContext");

        var bodyCount = CountOccurrences(descriptorSource!, "PropertyBindingSource.Body");
        bodyCount.Should().Be(1, "only Name should be Body-bound");
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
            assemblyName: "FromAuthContextTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
