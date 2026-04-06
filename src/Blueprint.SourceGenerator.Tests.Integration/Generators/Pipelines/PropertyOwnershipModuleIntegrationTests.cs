using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves generated population stage binds route and body properties end-to-end.
/// </summary>
public class PropertyOwnershipModuleIntegrationTests
{
    [Fact]
    public void When_post_with_route_and_body_then_descriptor_has_ownership_feature_data()
    {
        var descriptor = UpdateUserPopulationCommand.Configure();

        // Verify the descriptor has ownership feature data
        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue("descriptor should contain property ownership feature data");

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Name" && e.Source == PropertyBindingSource.Body);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Email" && e.Source == PropertyBindingSource.Body);
    }

    [Fact]
    public void When_get_query_with_route_param_then_remaining_properties_are_query_owned()
    {
        var descriptor = GetUserPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);

        // GET query: remaining properties should be Query-owned
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Name" && e.Source == PropertyBindingSource.Query);

        ownershipData.Entries.Should().NotContain(
            static e => e.Source == PropertyBindingSource.Body);
    }

    [Fact]
    public void When_route_property_is_int_then_descriptor_shows_route_ownership()
    {
        var descriptor = GetUserByIntIdQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);
    }

    [Fact]
    public void When_http_method_metadata_set_then_descriptor_reports_correct_methods()
    {
        var descriptor = UpdateUserPopulationCommand.Configure();

        descriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methodData)
            .Should().BeTrue();

        methodData!.HttpMethods.Should().Contain("POST");
    }

    [Fact]
    public void When_query_only_get_then_descriptor_has_query_ownership()
    {
        var descriptor = SearchPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Term" && e.Source == PropertyBindingSource.Query);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "PageSize" && e.Source == PropertyBindingSource.Query);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "IncludeArchived" && e.Source == PropertyBindingSource.Query);

        ownershipData.Entries.Should().NotContain(
            static e => e.Source == PropertyBindingSource.Body);
        ownershipData.Entries.Should().NotContain(
            static e => e.Source == PropertyBindingSource.Route);
    }

    [Fact]
    public void When_query_only_get_then_query_binding_names_are_camel_case()
    {
        var descriptor = SearchPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "PageSize" && e.BindingName == "pageSize");
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "IncludeArchived" && e.BindingName == "includeArchived");
    }

    [Fact]
    public void When_array_query_param_then_descriptor_has_query_ownership()
    {
        var descriptor = FilterPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Tags" && e.Source == PropertyBindingSource.Query);
    }

    [Fact]
    public void When_delete_with_route_and_query_then_descriptor_has_both()
    {
        var descriptor = DeleteItemPopulationCommand.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Force" && e.Source == PropertyBindingSource.Query);
    }

    [Fact]
    public void When_explicit_FromHeader_then_descriptor_has_header_ownership_with_binding_name()
    {
        var descriptor = HeaderBoundPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "TenantId" && e.Source == PropertyBindingSource.Header && e.BindingName == "X-Tenant-Id");
    }

    [Fact]
    public void When_explicit_FromCookie_then_descriptor_has_cookie_ownership_with_binding_name()
    {
        var descriptor = CookieBoundPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "SessionId" && e.Source == PropertyBindingSource.Cookie && e.BindingName == "session");
    }

    [Fact]
    public void When_explicit_FromQuery_with_name_on_post_then_descriptor_has_query_with_custom_binding_name()
    {
        var descriptor = ExplicitQueryPopulationCommand.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        // [FromQuery(Name = "q")] overrides camelCase convention
        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "SearchTerm" && e.Source == PropertyBindingSource.Query && e.BindingName == "q");

        // Remaining property on POST should still be Body-owned
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Title" && e.Source == PropertyBindingSource.Body);
    }

    [Fact]
    public void When_mixed_explicit_and_convention_then_descriptor_has_all_sources()
    {
        var descriptor = MixedBindingPopulationCommand.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        // Route: convention
        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "OrgId" && e.Source == PropertyBindingSource.Route);

        // Query: explicit [FromQuery] on POST
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Filter" && e.Source == PropertyBindingSource.Query && e.BindingName == "filter");

        // Header: explicit
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "TenantId" && e.Source == PropertyBindingSource.Header && e.BindingName == "X-Tenant");

        // Cookie: explicit
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Token" && e.Source == PropertyBindingSource.Cookie && e.BindingName == "auth_token");

        // Body: convention (remaining unowned on POST)
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Name" && e.Source == PropertyBindingSource.Body);
    }
}

// Test operations — these are discovered by the source generator at compile time.

[Blueprint.Model.Attributes.RootLink("/population-test/users-update/{Id}")]
public partial class UpdateUserPopulationCommand : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(UpdateUserPopulationCommand operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Id}:{operation.Name}:{operation.Email}");
    }
}

[Blueprint.Model.Attributes.RootLink("/population-test/users/{Id}")]
public partial class GetUserPopulationQuery : IQuery<string>
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public static Task<string> HandleAsync(GetUserPopulationQuery operation)
    {
        return Task.FromResult($"{operation.Id}:{operation.Name}");
    }
}

[Blueprint.Model.Attributes.RootLink("/population-test/users-int/{Id}")]
public partial class GetUserByIntIdQuery : IQuery<string>
{
    public int Id { get; set; }

    public static Task<string> HandleAsync(GetUserByIntIdQuery operation)
    {
        return Task.FromResult($"user-{operation.Id}");
    }
}

/// <summary>
/// Query-only GET with no route params — all bindable properties are query-owned.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/population-test/search")]
public partial class SearchPopulationQuery : IQuery<string>
{
    /// <summary>The search term.</summary>
    public string? Term { get; set; }

    /// <summary>Page size for pagination.</summary>
    public int PageSize { get; set; }

    /// <summary>Whether to include archived items.</summary>
    public bool IncludeArchived { get; set; }

    public static Task<string> HandleAsync(SearchPopulationQuery operation)
    {
        return Task.FromResult($"{operation.Term}:{operation.PageSize}:{operation.IncludeArchived}");
    }
}

/// <summary>
/// GET with array query params for testing multi-value query strings.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/population-test/filter")]
public partial class FilterPopulationQuery : IQuery<string>
{
    /// <summary>Tag IDs to filter by.</summary>
    public string[]? Tags { get; set; }

    public static Task<string> HandleAsync(FilterPopulationQuery operation)
    {
        return Task.FromResult(operation.Tags is not null ? string.Join(",", operation.Tags) : "none");
    }
}

/// <summary>
/// DELETE with route + query params for testing DELETE query binding.
/// </summary>
[Blueprint.Model.Attributes.HttpDelete]
[Blueprint.Model.Attributes.RootLink("/population-test/items/{Id}")]
public partial class DeleteItemPopulationCommand : ICommand<string>
{
    /// <summary>The item identifier.</summary>
    public string? Id { get; set; }

    /// <summary>Whether to force-delete the item.</summary>
    public bool Force { get; set; }

    public static Task<string> HandleAsync(DeleteItemPopulationCommand operation)
    {
        return Task.FromResult($"{operation.Id}:{operation.Force}");
    }
}

/// <summary>
/// GET query with explicit [FromHeader] for testing header binding in descriptors.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/population-test/header-bound")]
public partial class HeaderBoundPopulationQuery : IQuery<string>
{
    [Blueprint.Model.Attributes.FromHeader("X-Tenant-Id")]
    public string? TenantId { get; set; }

    public static Task<string> HandleAsync(HeaderBoundPopulationQuery operation)
    {
        return Task.FromResult($"tenant:{operation.TenantId}");
    }
}

/// <summary>
/// GET query with explicit [FromCookie] for testing cookie binding in descriptors.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/population-test/cookie-bound")]
public partial class CookieBoundPopulationQuery : IQuery<string>
{
    [Blueprint.Model.Attributes.FromCookie("session")]
    public string? SessionId { get; set; }

    public static Task<string> HandleAsync(CookieBoundPopulationQuery operation)
    {
        return Task.FromResult($"session:{operation.SessionId}");
    }
}

/// <summary>
/// POST command with explicit [FromQuery(Name = "q")] overriding the convention on a
/// write-method operation, plus a remaining body-owned property.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/population-test/explicit-query")]
public partial class ExplicitQueryPopulationCommand : ICommand<string>
{
    [Blueprint.Model.Attributes.FromQuery(Name = "q")]
    public string? SearchTerm { get; set; }

    /// <summary>Body-owned by convention.</summary>
    public string? Title { get; set; }

    public static Task<string> HandleAsync(ExplicitQueryPopulationCommand operation)
    {
        return Task.FromResult($"{operation.SearchTerm}:{operation.Title}");
    }
}

/// <summary>
/// POST command combining all binding sources: route (convention), query (explicit),
/// header (explicit), cookie (explicit), and body (convention remaining).
/// </summary>
[Blueprint.Model.Attributes.RootLink("/population-test/mixed/{OrgId}")]
public partial class MixedBindingPopulationCommand : ICommand<string>
{
    /// <summary>Route-owned by convention (placeholder match).</summary>
    public string? OrgId { get; set; }

    /// <summary>Query-owned via explicit attribute on a POST command.</summary>
    [Blueprint.Model.Attributes.FromQuery]
    public string? Filter { get; set; }

    /// <summary>Header-owned with explicit binding name.</summary>
    [Blueprint.Model.Attributes.FromHeader("X-Tenant")]
    public string? TenantId { get; set; }

    /// <summary>Cookie-owned with explicit binding name.</summary>
    [Blueprint.Model.Attributes.FromCookie("auth_token")]
    public string? Token { get; set; }

    /// <summary>Body-owned by convention (remaining unowned on POST).</summary>
    public string? Name { get; set; }

    public static Task<string> HandleAsync(MixedBindingPopulationCommand operation)
    {
        return Task.FromResult($"{operation.OrgId}:{operation.Filter}:{operation.TenantId}:{operation.Token}:{operation.Name}");
    }
}
