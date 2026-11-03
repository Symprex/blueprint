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
    private const string Preamble = "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
        + "namespace TestNamespace;\n\n";

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

        // Query string builder should handle non-route params using scalar separator pattern
        source.Should().Contain("typed.Locale");
        source.Should().Contain("typed.Page");
        source.Should().Contain("sep = '&'");
    }

    [Fact]
    public void When_get_query_with_route_and_query_params_then_emits_typed_builders_the_delegates_wrap()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }
                public int Page { get; set; }

                public static Task<string> HandleAsync(GetWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetWidgetQuery");
        source.Should().Contain("public static class Http");
        source.Should().Contain("public static string BuildUrl(global::TestNamespace.GetWidgetQuery operation)");
        source.Should().Contain("[return: global::System.Diagnostics.CodeAnalysis.MaybeNull]");
        source.Should().Contain("public static string AppendQueryString(global::TestNamespace.GetWidgetQuery operation, string url)");
        source.Should().NotContain("EditorBrowsable");

        // The delegates hold no URL logic of their own: they cast and call the typed builders.
        source.Should().Contain("static (op) => global::TestNamespace.GetWidgetQuery.Http.BuildUrl((global::TestNamespace.GetWidgetQuery)op)");
        source.Should().Contain("static (op, url) => global::TestNamespace.GetWidgetQuery.Http.AppendQueryString((global::TestNamespace.GetWidgetQuery)op, url)");
    }

    [Fact]
    public void When_post_command_then_typed_url_builder_but_no_typed_query_appender()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.HttpPost]
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class UpdateWidgetCommand : ICommand<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(UpdateWidgetCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var source = GetDescriptorSource(result, "UpdateWidgetCommand");
        source.Should().Contain("BuildUrl(global::TestNamespace.UpdateWidgetCommand operation)");
        source.Should().NotContain("AppendQueryString");
    }

    [Fact]
    public void When_operation_has_no_route_then_no_typed_builders_emitted()
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

        GetDescriptorSource(result, "InternalCommand").Should().NotContain("class Http");
    }

    [Fact]
    public void When_operation_declares_its_own_Http_member_then_no_nested_class_and_delegates_are_inline()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }

                public string Http { get; set; }

                public static Task<string> HandleAsync(GetWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetWidgetQuery");
        source.Should().NotContain("class Http");
        source.Should().Contain("static (op) => { var operation = (global::TestNamespace.GetWidgetQuery)op; var typed = operation; if (typed.Id is null) return null;");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void When_another_partial_declaration_gains_or_loses_an_Http_member_then_a_reused_driver_follows_it(bool startsWithMember)
    {
        // The operation's own file is the same in every revision. Its descriptor used to be keyed on that file's text
        // alone, so the driver answered the second revision from the plan it cached for the first: a nested class stayed
        // beside a new member (CS0102), and a removed member did not bring the class back, until a clean build.
        var operationFile = ("GetWidgetQuery.cs", Preamble + """
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
        var httpMemberFile = ("GetWidgetQuery.Http.cs", Preamble + """
            public partial class GetWidgetQuery
            {
                public string Http { get; set; }
            }
            """);

        (string FilePath, string Source)[] withMember = [operationFile, httpMemberFile];
        (string FilePath, string Source)[] withoutMember = [operationFile];

        var runs = TestHelpers.CompileWithTrackedGeneratorsAcrossRevisions(
            [TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>()],
            startsWithMember ? withMember : withoutMember,
            startsWithMember ? withoutMember : withMember,
            startsWithMember ? withMember : withoutMember);

        AssertNestedHttpClass(runs[0], declared: !startsWithMember);
        AssertNestedHttpClass(runs[1], declared: startsWithMember);
        AssertNestedHttpClass(runs[2], declared: !startsWithMember);

        static void AssertNestedHttpClass(MultiGeneratorGenerationResult run, bool declared)
        {
            run.Diagnostics.Should().BeEmpty("the generated partial must never declare a second Http beside the user's");

            var source = DescriptorSourceOf(run, "GetWidgetQuery");
            if (declared)
            {
                source.Should().Contain("public static class Http")
                    .And.Contain("static (op) => global::TestNamespace.GetWidgetQuery.Http.BuildUrl((global::TestNamespace.GetWidgetQuery)op)");
            }
            else
            {
                source.Should().NotContain("class Http")
                    .And.Contain("static (op) => { var operation = (global::TestNamespace.GetWidgetQuery)op; var typed = operation;");
            }
        }
    }

    [Fact]
    public void When_an_inherited_Http_property_is_a_route_placeholder_then_no_nested_class_hides_it()
    {
        // A nested Http class used to hide the inherited property, so the builder's own `typed.Http` named the class (CS0572).
        var result = CompileDescriptor(
            """
            public abstract class WidgetRouteBase
            {
                public string Http { get; set; }
            }

            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Http}")]
            public partial class GetWidgetByHttpQuery : WidgetRouteBase, IQuery<string>
            {
                public static Task<string> HandleAsync(GetWidgetByHttpQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetWidgetByHttpQuery");
        source.Should().NotContain("class Http");
        source.Should().Contain("static (op) => { var operation = (global::TestNamespace.GetWidgetByHttpQuery)op; var typed = operation; if (typed.Http is null) return null;");
        source.Should().Contain("EscapeDataString(typed.Http)");
    }

    [Fact]
    public void When_an_inherited_Http_property_is_a_query_parameter_then_no_nested_class_hides_it()
    {
        var result = CompileDescriptor(
            """
            public abstract class WidgetFilterBase
            {
                public string Http { get; set; }
            }

            [global::Blueprint.Model.Attributes.RootLink("/widgets")]
            public partial class ListWidgetsByHttpQuery : WidgetFilterBase, IQuery<string>
            {
                public static Task<string> HandleAsync(ListWidgetsByHttpQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "ListWidgetsByHttpQuery");
        source.Should().NotContain("class Http");
        source.Should().Contain("static (op, url) => { var operation = (global::TestNamespace.ListWidgetsByHttpQuery)op; var typed = operation;");
        source.Should().Contain("\"http=\" + global::System.Uri.EscapeDataString(typed.Http)");
    }

    [Fact]
    public void When_the_operation_is_itself_named_Http_then_no_nested_class_and_delegates_are_inline()
    {
        // A nested type cannot share its enclosing type's name (CS0542).
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/http/{Id}")]
            public partial class Http : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(Http operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "Http");
        source.Should().NotContain("static class Http");
        source.Should().Contain("static (op) => { var operation = (global::TestNamespace.Http)op; var typed = operation; if (typed.Id is null) return null;");
    }

    [Theory]
    [InlineData("private string Http { get; set; }", true)]
    [InlineData("protected string Http { get; set; }", false)]
    [InlineData("internal string Http { get; set; }", false)]
    [InlineData("public string Http() => \"\";", false)]
    [InlineData("public class Http { }", false)]
    public void When_a_base_type_declares_an_Http_member_then_the_nested_class_is_emitted_only_when_the_operation_cannot_see_it(
        string baseMember,
        bool emitsNestedClass)
    {
        var result = CompileDescriptor(
            $$"""
            public abstract class WidgetBase
            {
                {{baseMember}}
            }

            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : WidgetBase, IQuery<string>
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
        if (emitsNestedClass)
        {
            source.Should().Contain("public static class Http");
        }
        else
        {
            source.Should().NotContain("public static class Http");
            source.Should().Contain("static (op) => { var operation = (global::TestNamespace.GetWidgetQuery)op; var typed = operation;");
        }
    }

    [Fact]
    public void When_a_base_operation_in_a_referenced_assembly_has_its_generated_Http_class_then_the_derived_operation_still_declares_its_own()
    {
        // A base operation's generated Http is a symbol only once it is compiled into another assembly; from the same
        // compilation it is invisible. Either way the derived operation declares its own, hiding the base's (CS0108 is
        // suppressed for that), so it is not an inherited member that blocks the nested class.
        var contract = TestHelpers.CreateReferenceFromSource(
            """
            using Blueprint.Model;

            namespace Contract;

            public class GetBaseWidgetQuery : IQuery<string>
            {
                public string Id { get; set; } = "";

                public static ApiOperationDescriptor Configure() => null!;

                public static class Http
                {
                    public static string BuildUrl(GetBaseWidgetQuery operation) => "/base-widgets/" + operation.Id;
                }
            }
            """,
            "HttpBuilderContract");

        var result = TestHelpers.CompileOnce<OperationDescriptorSourceGenerator>(
            Preamble + """
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class GetDerivedWidgetQuery : global::Contract.GetBaseWidgetQuery
            {
                public static Task<string> HandleAsync(GetDerivedWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """,
            additionalReferences: [contract]);

        result.Diagnostics.Should().BeEmpty();
        GetDescriptorSource(result, "GetDerivedWidgetQuery").Should().Contain("public static class Http")
            .And.Contain("public static string BuildUrl(global::TestNamespace.GetDerivedWidgetQuery operation)");
    }

    [Theory]
    [InlineData("[global::Blueprint.Model.Attributes.FromHeader(\"X-Token\")] public string Token { get; set; }", "Token")]
    [InlineData("[global::Blueprint.Model.Attributes.FromCookie(\"session\")] public string Session { get; set; }", "Session")]
    [InlineData("[global::Blueprint.Model.Attributes.FromAuthContext] public string UserId { get; set; }", "UserId")]
    [InlineData("[global::Blueprint.Model.Attributes.FromAuthContext(ClaimType = \"sub\")] public string Subject { get; set; }", "Subject")]
    [InlineData("[global::Blueprint.Model.Attributes.FromAuthContext] public string[] Roles { get; set; }", "Roles")]
    public void When_a_get_has_no_query_owned_property_then_one_ownership_binds_elsewhere_stays_out_of_the_query_string(
        string propertyDeclaration,
        string propertyName)
    {
        // With nothing query-owned, the appender used to fall back to every non-placeholder property, so a header's token,
        // a cookie or a claim-bound property was written into the URL.
        var result = CompileDescriptor(
            $$"""
            [global::Blueprint.Model.Attributes.RootLink("/widgets")]
            public partial class ListWidgetsQuery : IQuery<string>
            {
                {{propertyDeclaration}}

                public static Task<string> HandleAsync(ListWidgetsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "ListWidgetsQuery");
        source.Should().NotContain($"typed.{propertyName}");
        source.Should().Contain("Http.BuildUrl((global::TestNamespace.ListWidgetsQuery)op), null));", "there is nothing to append");
    }

    [Fact]
    public void When_a_get_has_no_query_owned_property_then_a_route_only_property_outside_the_built_url_stays_out_of_the_query_string()
    {
        // An explicit [FromRoute] property has no query fallback, so the host never reads it from the query string.
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/widgets")]
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}", Rel = "widget-by-id")]
            public partial class ListWidgetsQuery : IQuery<string>
            {
                [global::Blueprint.Model.Attributes.FromRoute]
                public string Id { get; set; }

                public static Task<string> HandleAsync(ListWidgetsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "ListWidgetsQuery");
        source.Should().Contain("return \"/widgets\";");
        source.Should().NotContain("\"id=\"");
    }

    [Fact]
    public void When_a_get_has_no_query_owned_property_then_a_placeholder_its_built_url_leaves_out_is_still_appended()
    {
        // A placeholder in only some links falls back to the query string on the host (legacy's every-link rule), so a
        // client building the link without it must still send it there.
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/widgets")]
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}", Rel = "widget-by-id")]
            public partial class ListWidgetsQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(ListWidgetsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "ListWidgetsQuery");
        source.Should().Contain("return \"/widgets\";");
        source.Should().Contain("qs = qs + sep + \"id=\" + global::System.Uri.EscapeDataString(typed.Id);");
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

    [Fact]
    public void When_query_has_array_property_then_emits_foreach_loop()
    {
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class SearchItemsQuery : IQuery<string>
            {
                public string[] Ids { get; set; }
                public string Filter { get; set; }

                public static Task<string> HandleAsync(SearchItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "SearchItemsQuery");
        source.Should().Contain("CompiledHttpRequestFeatureData");

        // Collection should use foreach to emit repeated keys
        source.Should().Contain("foreach (var _item in typed.Ids)");
        source.Should().Contain("ids=");
    }

    [Fact]
    public void When_query_has_collection_of_custom_structs_then_each_elements_ToString_is_checked_before_the_key_is_added()
    {
        var result = CompileDescriptor(
            """
            [System.ComponentModel.TypeConverter(typeof(System.ComponentModel.TypeConverter))]
            public readonly struct ItemId
            {
                private readonly string _value;
                public override string ToString() => _value;
            }

            [System.ComponentModel.TypeConverter(typeof(System.ComponentModel.TypeConverter))]
            public class ItemIds : System.Collections.Generic.List<ItemId> { }

            [System.ComponentModel.TypeConverter(typeof(System.ComponentModel.TypeConverter))]
            public class Numbers : System.Collections.Generic.List<int> { }

            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class SearchItemsQuery : IQuery<string>
            {
                public ItemIds Ids { get; set; }
                public Numbers Numbers { get; set; }

                public static Task<string> HandleAsync(SearchItemsQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().NotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        var source = GetDescriptorSource(result, "SearchItemsQuery");

        source.Should().Contain("foreach (var _item in typed.Ids) { { var s = _item.ToString(); if (!string.IsNullOrEmpty(s)) parts.Add(\"ids=\"");
        source.Should().Contain("parts.Add(\"numbers=\" + global::System.Uri.EscapeDataString(_item.ToString()!));");
    }

    [Fact]
    public void When_query_has_custom_struct_and_nullable_struct_then_ToString_is_checked_and_built_in_values_are_not()
    {
        var result = CompileDescriptor(
            """
            [System.ComponentModel.TypeConverter(typeof(System.ComponentModel.TypeConverter))]
            public readonly struct ItemId
            {
                private readonly string _value;
                public override string ToString() => _value;
            }

            [global::Blueprint.Model.Attributes.RootLink("/items")]
            public partial class GetItemQuery : IQuery<string>
            {
                public ItemId Id { get; set; }
                public ItemId? Other { get; set; }
                public int Page { get; set; }

                public static Task<string> HandleAsync(GetItemQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var source = GetDescriptorSource(result, "GetItemQuery");

        source.Should().Contain("{ var s = typed.Id.ToString(); if (!string.IsNullOrEmpty(s))");
        source.Should().Contain("{ var s = typed.Other.Value.ToString(); if (!string.IsNullOrEmpty(s))");
        source.Should().Contain("EscapeDataString(typed.Page.ToString()!)");
    }

    [Fact]
    public void When_delete_command_with_non_url_params_then_no_query_string_builder()
    {
        // Legacy's client added the other properties as a query string for a GET alone and sent every other method's
        // operation as its JSON body (Signature365 8b40ecc961, Common.Core/Api/BlueprintHttpClient.cs:79, :88-91).
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.HttpDelete]
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id}")]
            public partial class DeleteWidgetCommand : ICommand<string>
            {
                public string Id { get; set; }
                public bool Permanent { get; set; }
                public string Reason { get; set; }

                public static Task<string> HandleAsync(DeleteWidgetCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "DeleteWidgetCommand");
        source.Should().Contain("CompiledHttpRequestFeatureData");

        // A DELETE's non-URL properties go in its body, as a POST's do.
        source.Should().Contain(", null)");
        source.Should().NotContain("typed.Permanent");
        source.Should().NotContain("typed.Reason");
    }

    [Fact]
    public void When_route_placeholder_has_a_legacy_format_specifier_then_its_property_is_still_bound()
    {
        // R111: {Name(format)} is route-owned via RoutePlaceholderSyntax (R97) — the bound name is whatever is before a
        // `:` or `(`. The old regex here only excluded `:`, so "Id(N)" was taken whole as the property name, matched no
        // property, and the placeholder was left in the built URL literally.
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/widgets/{Id(N)}")]
            public partial class GetFormattedWidgetQuery : IQuery<string>
            {
                public string Id { get; set; }

                public static Task<string> HandleAsync(GetFormattedWidgetQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetFormattedWidgetQuery");
        source.Should().Contain("CompiledHttpRequestFeatureData");
        source.Should().Contain("typed.Id is null");
        source.Should().Contain("EscapeDataString(typed.Id)");
    }

    [Fact]
    public void When_route_has_a_catch_all_placeholder_then_its_property_is_not_url_escaped()
    {
        // R111: isCatchAll is now read off the placeholder's own text (RoutePlaceholderSyntax.Placeholder has no
        // separate "star" group), not a regex group — guards that derivation.
        var result = CompileDescriptor(
            """
            [global::Blueprint.Model.Attributes.RootLink("/files/{*FolderPath}")]
            public partial class GetFileQuery : IQuery<string>
            {
                public string FolderPath { get; set; }

                public static Task<string> HandleAsync(GetFileQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var source = GetDescriptorSource(result, "GetFileQuery");
        source.Should().Contain("typed.FolderPath");
        source.Should().NotContain("EscapeDataString(typed.FolderPath");
    }

    private static GenerationResult CompileDescriptor(string source)
    {
        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(Preamble + source);
    }

    private static string DescriptorSourceOf(MultiGeneratorGenerationResult run, string operationName)
    {
        return run.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator))
            .Single(source => source.HintName == $"TestNamespace.{operationName}_Descriptor.g.cs")
            .SourceText;
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
