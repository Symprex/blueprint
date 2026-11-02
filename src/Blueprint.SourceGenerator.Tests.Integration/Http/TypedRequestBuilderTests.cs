using System.Reflection;
using Blueprint.Model;
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

    private static CompiledHttpRequestFeatureData CompiledFeatureOf(IOperation operation)
    {
        return new IntegrationTestBlueprintApp().FindOperation(operation.GetType())!
            .GetFeatureData<CompiledHttpRequestFeatureData>()!;
    }
}
