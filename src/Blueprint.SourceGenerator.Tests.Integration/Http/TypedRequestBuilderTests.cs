using System.Reflection;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the typed <c>Http.BuildUrl</c>/<c>Http.AppendQueryString</c> builders the descriptor generator emits on each
/// operation build exactly what the <see cref="CompiledHttpRequestFeatureData"/> delegates, which wrap them, build.
/// </summary>
public sealed class TypedRequestBuilderTests
{
    [Fact]
    public void A_route_placeholder_is_built_into_the_url_as_the_delegate_builds_it()
    {
        var operation = new BindTenantIdRouteQuery { TenantId = BoundTenantId.Hydrate("tenant_a b") };

        var typed = BindTenantIdRouteQuery.Http.BuildUrl(operation);

        typed.Should().Be("/tenant-binding/tenants/tenant_a%20b");
        typed.Should().Be(CompiledFeatureOf(operation).BuildUrl(operation));
    }

    [Fact]
    public void A_get_appends_its_query_properties_as_the_delegate_appends_them()
    {
        var operation = new BindTenantIdQuery { TenantId = BoundTenantId.Hydrate("t1"), Filter = "a&b" };

        var url = BindTenantIdQuery.Http.BuildUrl(operation)!;
        var typed = BindTenantIdQuery.Http.AppendQueryString(operation, url);

        typed.Should().Be("/tenant-binding/queries?tenantId=t1&filter=a%26b");
        typed.Should().Be(CompiledFeatureOf(operation).AppendQueryString!(operation, url));
    }

    [Fact]
    public void A_get_whose_properties_are_all_in_the_route_appends_nothing()
    {
        var operation = new BindTenantIdRouteQuery { TenantId = BoundTenantId.Hydrate("t1") };

        var url = BindTenantIdRouteQuery.Http.BuildUrl(operation)!;

        BindTenantIdRouteQuery.Http.AppendQueryString(operation, url).Should().Be(url);
    }

    [Fact]
    public void A_non_get_operation_has_a_typed_url_builder_but_no_typed_query_appender()
    {
        var http = typeof(BindTenantIdBodyCommand).GetNestedType("Http", BindingFlags.Public);

        http.Should().NotBeNull();
        http!.GetMethod("BuildUrl", BindingFlags.Public | BindingFlags.Static).Should().NotBeNull();
        http.GetMethod("AppendQueryString", BindingFlags.Public | BindingFlags.Static).Should().BeNull();
    }

    [Fact]
    public void An_inherited_Http_route_property_is_built_into_the_url_with_no_typed_builder_to_hide_it()
    {
        var operation = new GetByInheritedHttpRouteQuery { Http = "a b" };

        typeof(GetByInheritedHttpRouteQuery).GetNestedType("Http", BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        CompiledFeatureOf(operation).BuildUrl(operation).Should().Be("/typed-builder/http-routes/a%20b");
    }

    [Fact]
    public void An_inherited_Http_query_property_is_appended_with_no_typed_builder_to_hide_it()
    {
        var operation = new ListByInheritedHttpFilterQuery { Http = "a&b" };
        var feature = CompiledFeatureOf(operation);

        typeof(ListByInheritedHttpFilterQuery).GetNestedType("Http", BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        feature.AppendQueryString!(operation, feature.BuildUrl(operation)!).Should().Be("/typed-builder/http-filters?http=a%26b");
    }

    [Fact]
    public void An_operation_named_Http_builds_its_url_with_no_typed_builder_of_the_same_name()
    {
        var operation = new TypedRequestBuilderNames.Http { Id = "x y" };

        typeof(TypedRequestBuilderNames.Http).GetNestedType("Http", BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        CompiledFeatureOf(operation).BuildUrl(operation).Should().Be("/typed-builder/http/x%20y");
    }

    [Fact]
    public void A_get_whose_properties_are_bound_from_a_header_and_the_auth_context_appends_nothing()
    {
        var operation = new GetWithHeaderAndAuthContextQuery { Token = "secret", UserId = "u-1" };
        var url = GetWithHeaderAndAuthContextQuery.Http.BuildUrl(operation)!;

        CompiledFeatureOf(operation).AppendQueryString.Should().BeNull("nothing of the operation's is bound from the query string");
        GetWithHeaderAndAuthContextQuery.Http.AppendQueryString(operation, url).Should().Be("/typed-builder/header-and-auth-context");
    }

    private static CompiledHttpRequestFeatureData CompiledFeatureOf(IOperation operation)
    {
        return new IntegrationTestBlueprintApp().FindOperation(operation.GetType())!
            .GetFeatureData<CompiledHttpRequestFeatureData>()!;
    }
}

/// <summary>
/// Not an operation: a base whose <c>Http</c> property its derived operations route or filter by, which a nested
/// <c>Http</c> builder class on them would hide.
/// </summary>
public abstract class TypedBuilderHttpPropertyBase
{
    public string? Http { get; set; }
}

/// <summary>A query routed by its inherited <c>Http</c> property.</summary>
[RootLink("/typed-builder/http-routes/{Http}")]
public partial class GetByInheritedHttpRouteQuery : TypedBuilderHttpPropertyBase, IQuery<string>
{
    public static string Handle(GetByInheritedHttpRouteQuery query)
    {
        return query.Http ?? "(unset)";
    }
}

/// <summary>A query with nothing in its query string: one property is bound from a header, the other by the auth pipeline.</summary>
[RootLink("/typed-builder/header-and-auth-context")]
public partial class GetWithHeaderAndAuthContextQuery : IQuery<string>
{
    [FromHeader("X-Token")]
    public string? Token { get; set; }

    [FromAuthContext]
    public string? UserId { get; set; }

    public static string Handle(GetWithHeaderAndAuthContextQuery query)
    {
        return query.UserId ?? "(unset)";
    }
}

/// <summary>A query filtered by its inherited <c>Http</c> property, in its query string.</summary>
[RootLink("/typed-builder/http-filters")]
public partial class ListByInheritedHttpFilterQuery : TypedBuilderHttpPropertyBase, IQuery<string>
{
    public static string Handle(ListByInheritedHttpFilterQuery query)
    {
        return query.Http ?? "(unset)";
    }
}
