using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Tests that <c>HttpClientRequestBuilderModule</c> emits correct
/// <c>CompiledHttpRequestFeatureData</c> for various operation shapes: simple route params,
/// query strings, nullable properties, booleans, mixed route+query, and no-route operations.
/// </summary>
public class HttpClientRequestBuilderModuleTests
{
    [Fact]
    public void When_get_query_with_route_param_then_emits_url_builder_with_null_check()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetWidgetQuery");
        source.Should().Contain("CompiledHttpRequestFeatureData");
        source.Should().Contain("typed.Id is null");
        source.Should().Contain("EscapeDataString(typed.Id)");
    }

    [Fact]
    public void When_get_query_with_query_params_then_emits_query_string_builder()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string Locale { get; set; }
                public int Page { get; set; }

                public static Task<string> HandleAsync(GetWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetWidgetQuery");
        source.Should().Contain("CompiledHttpRequestFeatureData");

        // Query string builder should handle non-route params
        source.Should().Contain("typed.Locale");
        source.Should().Contain("typed.Page");
        source.Should().Contain("parts.Add(");
    }

    [Fact]
    public void When_post_command_then_no_query_string_builder()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.HttpPost]
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class UpdateWidgetCommand : ICommand<string>
            {
                public string Id { get; set; }
                public string Name { get; set; }

                public static Task<string> HandleAsync(UpdateWidgetCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "UpdateWidgetCommand");
        source.Should().Contain("CompiledHttpRequestFeatureData");

        // POST should not build query strings — body properties only
        source.Should().Contain(", null)");
    }

    [Fact]
    public void When_operation_has_no_route_then_no_feature_data_emitted()
    {
        var result = CompileDescriptor(
            """
            public partial class InternalCommand : ICommand<string>
            {
                public string Data { get; set; }

                public static Task<string> HandleAsync(InternalCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "InternalCommand");

        // No URL registration means no CompiledHttpRequestFeatureData
        source.Should().NotContain("CompiledHttpRequestFeatureData");
    }

    [Fact]
    public void When_route_has_multiple_params_then_url_builder_concatenates_all_segments()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/tenants/{TenantId}/users/{UserId}")]
            public partial class GetTenantUserQuery : IQuery<string>
            {
                public string TenantId { get; set; }
                public string UserId { get; set; }

                public static Task<string> HandleAsync(GetTenantUserQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetTenantUserQuery");
        source.Should().Contain("CompiledHttpRequestFeatureData");
        source.Should().Contain("typed.TenantId is null");
        source.Should().Contain("typed.UserId is null");
        source.Should().Contain("\"/tenants/\"");
        source.Should().Contain("\"/users/\"");
    }

    [Fact]
    public void When_route_param_is_int_then_no_null_check_emitted()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items/{Id}")]
            public partial class GetItemQuery : IQuery<string>
            {
                public int Id { get; set; }

                public static Task<string> HandleAsync(GetItemQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetItemQuery");
        source.Should().Contain("CompiledHttpRequestFeatureData");

        // int is a non-nullable value type — no null check
        source.Should().NotContain("typed.Id is null");
        source.Should().Contain("EscapeDataString(typed.Id.ToString()!)");
    }

    [Fact]
    public void When_route_param_is_guid_then_value_type_to_string_used()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items/{Id}")]
            public partial class GetItemByGuidQuery : IQuery<string>
            {
                public System.Guid Id { get; set; }

                public static Task<string> HandleAsync(GetItemByGuidQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetItemByGuidQuery");
        source.Should().Contain("EscapeDataString(typed.Id.ToString()!)");

        // Guid is non-nullable value type — no null check
        source.Should().NotContain("typed.Id is null");
    }

    [Fact]
    public void When_query_has_boolean_param_then_emits_true_false_strings()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/search")]
            public partial class SearchQuery : IQuery<string>
            {
                public bool IncludeArchived { get; set; }
                public string Term { get; set; }

                public static Task<string> HandleAsync(SearchQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "SearchQuery");
        source.Should().Contain("CompiledHttpRequestFeatureData");
        source.Should().Contain("\"true\" : \"false\"");
    }

    [Fact]
    public void When_property_is_obsolete_then_excluded_from_query_string()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class ListItemsQuery : IQuery<string>
            {
                public string Filter { get; set; }

                [System.Obsolete("Use Filter instead")]
                public string OldFilter { get; set; }

                public static Task<string> HandleAsync(ListItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "ListItemsQuery");
        source.Should().Contain("typed.Filter");
        source.Should().NotContain("typed.OldFilter");
    }

    private static GenerationResult CompileDescriptor(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
            + "namespace TestNamespace;\n\n" + source);
    }

    private static string GetDescriptorSource(GenerationResult result, string operationName)
    {
        var source = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(s => s.Contains($"{operationName}Descriptor") || s.Contains("CompiledHttpRequestFeatureData"));

        source.Should().NotBeNull($"descriptor output for {operationName} should exist");
        return source!;
    }
}
