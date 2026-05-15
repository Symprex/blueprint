using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
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
    public void When_self_link_route_parameter_exists_then_descriptor_shows_route_ownership()
    {
        var descriptor = SelfLinkRoutePopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);
    }

    [Fact]
    public async Task When_route_uses_catch_all_then_pipeline_populates_full_path_from_route_values()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetFolderByPathPopulationQuery));
        var descriptor = GetFolderByPathPopulationQuery.Configure();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["FolderPath"] = "UK/Marketing";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new GetFolderByPathPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("UK/Marketing");
    }

    [Fact]
    public void When_route_parameter_is_IParsable_type_then_descriptor_shows_route_ownership()
    {
        var descriptor = GetItemByStrongIdPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "ItemId" && e.Source == PropertyBindingSource.Route);
    }

    [Fact]
    public async Task When_route_parameter_is_IParsable_type_then_pipeline_parses_from_route_value()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetItemByStrongIdPopulationQuery));
        var descriptor = GetItemByStrongIdPopulationQuery.Configure();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["ItemId"] = "item-42";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new GetItemByStrongIdPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("item-42");
    }

    [Fact]
    public async Task When_route_parameter_is_IParsable_type_and_value_is_invalid_then_binding_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetItemByStrictIdPopulationQuery));
        var descriptor = GetItemByStrictIdPopulationQuery.Configure();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["ItemId"] = "INVALID_NOT_NUMERIC";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new GetItemByStrictIdPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.BindingFailureResult>();
    }

    [Fact]
    public void When_query_parameter_type_implements_IParsable_then_descriptor_has_query_ownership()
    {
        var descriptor = SearchItemsByStrongIdPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should()
            .Contain(static b => b.PropertyName == "FilterId" && b.Source == PropertyBindingSource.Query);
    }

    [Fact]
    public async Task When_query_parameter_type_implements_IParsable_and_value_provided_then_parses_correctly()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(SearchItemsByStrongIdPopulationQuery));
        var descriptor = SearchItemsByStrongIdPopulationQuery.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?FilterId=custom-id-42");

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new SearchItemsByStrongIdPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        var query = (SearchItemsByStrongIdPopulationQuery)context.Operation;
        query.FilterId.Should().NotBeNull();
        query.FilterId!.Value.Should().Be("custom-id-42");
    }

    [Fact]
    public async Task When_route_parameter_is_enum_then_pipeline_parses_enum_value()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetItemByEnumCategoryPopulationQuery));
        var descriptor = GetItemByEnumCategoryPopulationQuery.Configure();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        httpContext.Request.RouteValues["Category"] = "Active";
        httpContext.Request.RouteValues["Id"] = "item-42";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new GetItemByEnumCategoryPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("Active:item-42");
    }

    [Fact]
    public async Task When_route_parameter_is_enum_and_value_invalid_then_binding_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetItemByEnumCategoryPopulationQuery));
        var descriptor = GetItemByEnumCategoryPopulationQuery.Configure();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        httpContext.Request.RouteValues["Category"] = "NOT_A_CATEGORY";
        httpContext.Request.RouteValues["Id"] = "item-42";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new GetItemByEnumCategoryPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.BindingFailureResult>();
    }

    [Fact]
    public async Task When_query_parameter_is_nullable_enum_and_value_provided_then_parses()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(SearchByEnumStatusPopulationQuery));
        var descriptor = SearchByEnumStatusPopulationQuery.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?Status=Archived");

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new SearchByEnumStatusPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("Archived");
    }

    [Fact]
    public async Task When_query_parameter_is_nullable_enum_and_not_provided_then_stays_null()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(SearchByEnumStatusPopulationQuery));
        var descriptor = SearchByEnumStatusPopulationQuery.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new SearchByEnumStatusPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("none");
    }

    [Fact]
    public async Task When_route_parameter_type_has_TypeConverter_then_pipeline_converts_from_route_value()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetItemByTypeConverterIdPopulationQuery));
        var descriptor = GetItemByTypeConverterIdPopulationQuery.Configure();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        var testId = TypeConverterId.Create();
        httpContext.Request.RouteValues["TenantId"] = testId.ToString();
        httpContext.Request.RouteValues["Id"] = "doc-123";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new GetItemByTypeConverterIdPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be($"{testId}:doc-123");
    }

    [Fact]
    public async Task When_route_parameter_type_has_TypeConverter_and_value_invalid_then_binding_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetItemByTypeConverterIdPopulationQuery));
        var descriptor = GetItemByTypeConverterIdPopulationQuery.Configure();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        httpContext.Request.RouteValues["TenantId"] = "NOT-A-VALID-GUID";
        httpContext.Request.RouteValues["Id"] = "doc-123";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new GetItemByTypeConverterIdPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.BindingFailureResult>();
    }

    [Fact]
    public async Task When_query_parameter_type_has_TypeConverter_and_value_provided_then_converts()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(SearchByTypeConverterIdPopulationQuery));
        var descriptor = SearchByTypeConverterIdPopulationQuery.Configure();

        var testId = TypeConverterId.Create();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString($"?FilterId={testId}");

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new SearchByTypeConverterIdPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be(testId.ToString());
    }

    [Fact]
    public async Task When_query_parameter_type_has_TypeConverter_and_not_provided_then_stays_null()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(SearchByTypeConverterIdPopulationQuery));
        var descriptor = SearchByTypeConverterIdPopulationQuery.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new SearchByTypeConverterIdPopulationQuery(),
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("none");
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
    public void When_explicit_FromQuery_with_constructor_name_then_descriptor_has_custom_binding_name()
    {
        var descriptor = ConstructorQueryPopulationCommand.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        // [FromQuery("validationToken")] constructor form overrides camelCase convention
        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Token" && e.Source == PropertyBindingSource.Query && e.BindingName == "validationToken");

        // Remaining property on POST should still be Body-owned
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Payload" && e.Source == PropertyBindingSource.Body);
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

    [Fact]
    public async Task When_route_binding_targets_init_only_property_then_pipeline_populates_init_value()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RouteInitOnlyPopulationQuery));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "route-init-123";

        var context = CreateContext(app, RouteInitOnlyPopulationQuery.Configure(), new RouteInitOnlyPopulationQuery { Id = string.Empty }, httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("route-init-123");
    }

    [Fact]
    public async Task When_query_binding_targets_init_only_property_then_pipeline_populates_init_value()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(QueryInitOnlyPopulationQuery));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?term=blueprint-init");

        var context = CreateContext(app, QueryInitOnlyPopulationQuery.Configure(), new QueryInitOnlyPopulationQuery { Term = string.Empty }, httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("blueprint-init");
    }

    [Fact]
    public async Task When_operation_has_mixed_init_and_set_properties_then_pipeline_populates_both()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(MixedInitAndSetPopulationQuery));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "mixed-init-456";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?filter=recent");

        var context = CreateContext(app, MixedInitAndSetPopulationQuery.Configure(), new MixedInitAndSetPopulationQuery { Id = string.Empty }, httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("mixed-init-456:recent");
    }

    [Fact]
    public async Task When_body_binding_targets_init_only_properties_then_pipeline_populates_init_values()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(BodyInitOnlyPopulationCommand));
        var json = "{\"title\":\"init-body\",\"count\":7}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = bytes.Length;
        httpContext.Request.Body = new MemoryStream(bytes);

        var context = CreateContext(app, BodyInitOnlyPopulationCommand.Configure(), new BodyInitOnlyPopulationCommand { Title = string.Empty, Count = -1 }, httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("init-body:7");
    }

    [Fact]
    public async Task When_post_inherits_route_property_from_base_class_then_pipeline_binds_inherited_route_and_body()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(CreateTenantItemPopulationCommand));
        var json = """{"itemName":"widget"}""";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = bytes.Length;
        httpContext.Request.Body = new MemoryStream(bytes);
        httpContext.Request.RouteValues["TenantId"] = "tenant-42";

        var context = CreateContext(app, CreateTenantItemPopulationCommand.Configure(), new CreateTenantItemPopulationCommand(), httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("tenant-42:widget");
    }

    [Fact]
    public async Task When_get_inherits_pagination_from_base_class_then_pipeline_binds_inherited_query_params()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(SearchItemsPagedPopulationQuery));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?term=hello&page=3&pageSize=25");

        var context = CreateContext(app, SearchItemsPagedPopulationQuery.Configure(), new SearchItemsPagedPopulationQuery(), httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("hello:p3:s25");
    }

    [Fact]
    public void When_post_inherits_route_property_then_descriptor_has_route_and_body_entries()
    {
        var descriptor = CreateTenantItemPopulationCommand.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "TenantId" && e.Source == PropertyBindingSource.Route);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "ItemName" && e.Source == PropertyBindingSource.Body);
    }

    [Fact]
    public void When_get_inherits_pagination_then_descriptor_has_query_entries_for_inherited_properties()
    {
        var descriptor = SearchItemsPagedPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Term" && e.Source == PropertyBindingSource.Query);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Page" && e.Source == PropertyBindingSource.Query);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "PageSize" && e.Source == PropertyBindingSource.Query);
    }

    [Fact]
    public async Task When_3_level_inheritance_then_pipeline_binds_all_ancestor_properties()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetTenantReportsPopulationQuery));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.RouteValues["TenantId"] = "acme-corp";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?category=finance&page=2&pageSize=10");

        var context = CreateContext(app, GetTenantReportsPopulationQuery.Configure(), new GetTenantReportsPopulationQuery(), httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("acme-corp:finance:p2:s10");
    }

    [Fact]
    public void When_3_level_inheritance_then_descriptor_has_all_ancestor_property_entries()
    {
        var descriptor = GetTenantReportsPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        // Level 3 (declared) — query-bound
        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Category" && e.Source == PropertyBindingSource.Query);

        // Level 2 (TenantPagedOperation) — route-bound
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "TenantId" && e.Source == PropertyBindingSource.Route);

        // Level 1 (BasePagedOperation) — query-bound
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Page" && e.Source == PropertyBindingSource.Query);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "PageSize" && e.Source == PropertyBindingSource.Query);
    }

    [Fact]
    public void When_post_command_has_FromAuthContext_then_descriptor_has_AuthContext_source()
    {
        var descriptor = CreateAuthContextItemCommand.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "TenantId" && e.Source == PropertyBindingSource.AuthContext,
            "[FromAuthContext] property should be AuthContext-owned");

        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Name" && e.Source == PropertyBindingSource.Body,
            "non-auth-context property should remain body-owned on POST");
    }

    [Fact]
    public void When_get_query_has_FromAuthContext_then_descriptor_has_AuthContext_source()
    {
        var descriptor = ListAuthContextItemsQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "TenantId" && e.Source == PropertyBindingSource.AuthContext,
            "[FromAuthContext] property should be AuthContext-owned on GET");

        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Page" && e.Source == PropertyBindingSource.Query,
            "non-auth-context property should remain query-owned on GET");
    }

    [Fact]
    public void When_FromAuthContext_property_is_route_placeholder_then_route_wins()
    {
        var descriptor = GetAuthContextRouteItemQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        // Route takes precedence over [FromAuthContext]
        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "TenantId" && e.Source == PropertyBindingSource.Route,
            "route placeholder should take precedence over [FromAuthContext]");

        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);

        ownershipData.Entries.Should().NotContain(
            static e => e.Source == PropertyBindingSource.AuthContext,
            "no AuthContext entries expected when route wins");
    }

    [Fact]
    public void When_FromAuthContext_has_ClaimType_then_descriptor_records_AuthContext_source()
    {
        var descriptor = GetClaimMappedItemQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "PartnerId" && e.Source == PropertyBindingSource.AuthContext,
            "[FromAuthContext(ClaimType)] property should be AuthContext-owned");

        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);
    }

    [Fact]
    public async Task When_claim_mapped_property_and_claim_present_then_property_populated_from_claim()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetClaimMappedItemQuery));
        var descriptor = GetClaimMappedItemQuery.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "item-42";

        // Set up authenticated user with partner_id claim
        var identity = new System.Security.Claims.ClaimsIdentity("TestAuth");
        identity.AddClaim(new System.Security.Claims.Claim("partner_id", "partner-abc"));
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

        var operation = new GetClaimMappedItemQuery();

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("id=item-42,partner=partner-abc");
    }

    [Fact]
    public async Task When_claim_mapped_property_and_claim_missing_then_property_stays_null()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetClaimMappedItemQuery));
        var descriptor = GetClaimMappedItemQuery.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "item-42";

        // Authenticated user WITHOUT the partner_id claim
        var identity = new System.Security.Claims.ClaimsIdentity("TestAuth");
        identity.AddClaim(new System.Security.Claims.Claim("sub", "user-123"));
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

        var operation = new GetClaimMappedItemQuery();

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.OkResult<string>>();
        ((Blueprint.Runtime.Exec.OkResult<string>)result).Content.Should().Be("id=item-42,partner=null");
    }

    [Fact]
    public async Task When_claim_mapped_property_and_user_not_authenticated_then_returns_401()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GetClaimMappedItemQuery));
        var descriptor = GetClaimMappedItemQuery.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "item-42";

        // Anonymous user — no ClaimsIdentity with auth type
        var operation = new GetClaimMappedItemQuery();

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<Blueprint.Runtime.Exec.UnauthenticatedResult>();
    }

    private static Blueprint.Runtime.Exec.ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor, object operation, Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        return new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
        };
    }
}

// Test operations — these are discovered by the source generator at compile time.

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/users-update/{Id}")]
public partial class UpdateUserPopulationCommand : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string Id { get; set; } = string.Empty;

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

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/users/{Id}")]
public partial class GetUserPopulationQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public static Task<string> HandleAsync(GetUserPopulationQuery operation)
    {
        return Task.FromResult($"{operation.Id}:{operation.Name}");
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/users-int/{Id}")]
public partial class GetUserByIntIdQuery : IQuery<string>
{
    public int Id { get; set; }

    public static Task<string> HandleAsync(GetUserByIntIdQuery operation)
    {
        return Task.FromResult($"user-{operation.Id}");
    }
}

[Blueprint.Model.Attributes.SelfLink(typeof(SelfLinkRoutePopulationApiResource), "/population-test/self/{Id}")]
public partial class SelfLinkRoutePopulationQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    public static Task<string> HandleAsync(SelfLinkRoutePopulationQuery operation)
    {
        return Task.FromResult(operation.Id ?? string.Empty);
    }
}

public partial class SelfLinkRoutePopulationApiResource : ApiResource
{
    public string Id { get; init; } = null!;
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/folders/{**FolderPath}")]
public partial class GetFolderByPathPopulationQuery : IQuery<string>
{
    public string? FolderPath { get; set; }

    public static Task<string> HandleAsync(GetFolderByPathPopulationQuery operation)
    {
        return Task.FromResult(operation.FolderPath ?? string.Empty);
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/init-route/{Id}")]
public partial class RouteInitOnlyPopulationQuery : IQuery<string>
{
    public required string Id { get; init; }

    public static Task<string> HandleAsync(RouteInitOnlyPopulationQuery operation)
    {
        return Task.FromResult(operation.Id);
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/init-query")]
public partial class QueryInitOnlyPopulationQuery : IQuery<string>
{
    public required string Term { get; init; }

    public static Task<string> HandleAsync(QueryInitOnlyPopulationQuery operation)
    {
        return Task.FromResult(operation.Term);
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/init-mixed/{Id}")]
public partial class MixedInitAndSetPopulationQuery : IQuery<string>
{
    public required string Id { get; init; }

    public string? Filter { get; set; }

    public static Task<string> HandleAsync(MixedInitAndSetPopulationQuery operation)
    {
        return Task.FromResult($"{operation.Id}:{operation.Filter}");
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/init-body")]
public partial class BodyInitOnlyPopulationCommand : ICommand<string>
{
    public required string Title { get; init; }

    public int Count { get; init; }

    public static Task<string> HandleAsync(BodyInitOnlyPopulationCommand operation)
    {
        return Task.FromResult($"{operation.Title}:{operation.Count}");
    }
}

/// <summary>
/// Query-only GET with no route params — all bindable properties are query-owned.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/items/{Id}")]
public partial class DeleteItemPopulationCommand : ICommand<string>
{
    /// <summary>The item identifier.</summary>
    public string Id { get; set; } = string.Empty;

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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
/// POST command with <c>[FromQuery("validationToken")]</c> constructor form — proves the
/// generator reads <c>ConstructorArguments</c> in addition to <c>NamedArguments</c> for the
/// custom binding name.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/constructor-query")]
public partial class ConstructorQueryPopulationCommand : ICommand<string>
{
    [Blueprint.Model.Attributes.FromQuery("validationToken")]
    public string? Token { get; set; }

    /// <summary>Body-owned by convention.</summary>
    public string? Payload { get; set; }

    public static Task<string> HandleAsync(ConstructorQueryPopulationCommand operation)
    {
        return Task.FromResult($"{operation.Token}:{operation.Payload}");
    }
}

/// <summary>
/// POST command combining all binding sources: route (convention), query (explicit),
/// header (explicit), cookie (explicit), and body (convention remaining).
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/mixed/{OrgId}")]
public partial class MixedBindingPopulationCommand : ICommand<string>
{
    /// <summary>Route-owned by convention (placeholder match).</summary>
    public string OrgId { get; set; } = string.Empty;

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

/// <summary>
/// A strongly-typed identifier that implements <see cref="IParsable{T}"/> for route/query binding.
/// Always succeeds parsing (accepts any string).
/// </summary>
public class StrongItemId : IParsable<StrongItemId>
{
    public string Value { get; set; } = string.Empty;

    public static StrongItemId Parse(string s, IFormatProvider? provider)
    {
        return new() { Value = s };
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out StrongItemId result)
    {
        result = new StrongItemId { Value = s ?? string.Empty };
        return true;
    }
}

/// <summary>
/// A strongly-typed identifier that rejects non-numeric strings to test binding failure paths.
/// </summary>
public class StrictNumericId : IParsable<StrictNumericId>
{
    public int Value { get; set; }

    public static StrictNumericId Parse(string s, IFormatProvider? provider)
    {
        return new() { Value = int.Parse(s, provider) };
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out StrictNumericId result)
    {
        if (int.TryParse(s, System.Globalization.NumberStyles.Integer, provider, out var parsed))
        {
            result = new StrictNumericId { Value = parsed };
            return true;
        }

        result = default!;
        return false;
    }
}

/// <summary>
/// GET with IParsable route parameter — tests that source-gen accepts IParsable types as route params.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/items/{ItemId}")]
public partial class GetItemByStrongIdPopulationQuery : IQuery<string>
{
    public StrongItemId ItemId { get; set; } = new();

    public static Task<string> HandleAsync(GetItemByStrongIdPopulationQuery operation)
    {
        return Task.FromResult(operation.ItemId.Value);
    }
}

/// <summary>
/// GET with strict IParsable route parameter — tests binding failure when TryParse returns false.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/strict-items/{ItemId}")]
public partial class GetItemByStrictIdPopulationQuery : IQuery<string>
{
    public StrictNumericId ItemId { get; set; } = new();

    public static Task<string> HandleAsync(GetItemByStrictIdPopulationQuery operation)
    {
        return Task.FromResult(operation.ItemId.Value.ToString());
    }
}

/// <summary>
/// GET with IParsable query parameter — tests that source-gen accepts IParsable types as query params.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/search-items")]
public partial class SearchItemsByStrongIdPopulationQuery : IQuery<string>
{
    [Blueprint.Model.Attributes.FromQuery]
    public StrongItemId? FilterId { get; set; }

    public static Task<string> HandleAsync(SearchItemsByStrongIdPopulationQuery operation)
    {
        return Task.FromResult(operation.FilterId?.Value ?? "none");
    }
}

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        return null;
    }
}

// --- Inherited property binding test operations ---

/// <summary>
/// Abstract base class providing common properties, simulating S365 patterns like
/// <c>DisclaimerCommand</c> or <c>TenantScopedCommand</c>.
/// </summary>
public abstract class TenantScopedOperation
{
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Operation inheriting route-bound property from <see cref="TenantScopedOperation"/>.
/// Proves that inherited properties are bound from route values at runtime.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/tenants/{TenantId}/items")]
public partial class CreateTenantItemPopulationCommand : TenantScopedOperation, ICommand<string>
{
    public string ItemName { get; set; } = string.Empty;

    public static Task<string> HandleAsync(CreateTenantItemPopulationCommand operation)
    {
        return Task.FromResult($"{operation.TenantId}:{operation.ItemName}");
    }
}

/// <summary>
/// Abstract paged query base with pagination properties, simulating <see cref="Blueprint.Model.OrderedPagedQuery"/>.
/// </summary>
public abstract class SimplifiedPagedQuery
{
    public int? Page { get; set; } = 1;

    public int? PageSize { get; set; } = 50;
}

/// <summary>
/// GET query inheriting pagination properties from <see cref="SimplifiedPagedQuery"/>.
/// Proves inherited properties on GET are query-bound at runtime.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/items/search")]
public partial class SearchItemsPagedPopulationQuery : SimplifiedPagedQuery, IQuery<string>
{
    public string? Term { get; set; }

    public static Task<string> HandleAsync(SearchItemsPagedPopulationQuery operation)
    {
        return Task.FromResult($"{operation.Term}:p{operation.Page}:s{operation.PageSize}");
    }
}

// --- Multi-level inheritance (3+ levels) test operations ---

/// <summary>
/// Level 1: Base paged query with pagination.
/// </summary>
public abstract class BasePagedOperation
{
    public int? Page { get; set; } = 1;

    public int? PageSize { get; set; } = 50;
}

/// <summary>
/// Level 2: Adds tenant scoping on top of paging.
/// </summary>
public abstract class TenantPagedOperation : BasePagedOperation
{
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Level 3: Concrete operation with its own properties plus two inherited levels.
/// Proves GetAllMembers() walks the entire 3-level type hierarchy.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/tenants/{TenantId}/reports")]
public partial class GetTenantReportsPopulationQuery : TenantPagedOperation, IQuery<string>
{
    public string? Category { get; set; }

    public static Task<string> HandleAsync(GetTenantReportsPopulationQuery operation)
    {
        return Task.FromResult($"{operation.TenantId}:{operation.Category}:p{operation.Page}:s{operation.PageSize}");
    }
}

// --- TypeConverter route/query binding test types ---

/// <summary>
/// A custom value type with <see cref="System.ComponentModel.TypeConverter"/> but no <c>IParsable</c> or
/// <c>TryParse</c>. Simulates S365's <c>ApiId</c> pattern.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(TypeConverterIdConverter))]
public readonly struct TypeConverterId : IEquatable<TypeConverterId>
{
    private readonly Guid _value;

    private TypeConverterId(Guid value)
    {
        this._value = value;
    }

    public static TypeConverterId Create()
    {
        return new(Guid.NewGuid());
    }

    public static TypeConverterId Hydrate(string raw)
    {
        return new(Guid.Parse(raw));
    }

    public bool Equals(TypeConverterId other)
    {
        return this._value == other._value;
    }

    public override bool Equals(object? obj)
    {
        return obj is TypeConverterId other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        return this._value.GetHashCode();
    }

    public override string ToString()
    {
        return this._value.ToString("N");
    }
}

/// <summary>
/// TypeConverter for <see cref="TypeConverterId"/> that enables string-to-struct conversion at runtime.
/// </summary>
public sealed class TypeConverterIdConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        return value is string s ? TypeConverterId.Hydrate(s) : base.ConvertFrom(context, culture, value);
    }
}

/// <summary>
/// GET with <c>[TypeConverter]</c> route parameter — proves the generated <c>TypeDescriptor.GetConverter()</c>
/// code path works at runtime.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/tenants/{TenantId}/tc-items/{Id}")]
public partial class GetItemByTypeConverterIdPopulationQuery : IQuery<string>
{
    public TypeConverterId TenantId { get; set; }

    public string Id { get; set; } = string.Empty;

    public static Task<string> HandleAsync(GetItemByTypeConverterIdPopulationQuery operation)
    {
        return Task.FromResult($"{operation.TenantId}:{operation.Id}");
    }
}

/// <summary>
/// GET with nullable <c>[TypeConverter]</c> query parameter — proves nullable TypeConverter
/// query binding works at runtime.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/tc-search")]
public partial class SearchByTypeConverterIdPopulationQuery : IQuery<string>
{
    public TypeConverterId? FilterId { get; set; }

    public static Task<string> HandleAsync(SearchByTypeConverterIdPopulationQuery operation)
    {
        return Task.FromResult(operation.FilterId?.ToString() ?? "none");
    }
}

/// <summary>
/// POST command with <c>[FromAuthContext]</c> on TenantId — proves the property is excluded
/// from body binding and recorded as <see cref="PropertyBindingSource.AuthContext"/> in the
/// descriptor, while other properties are still body-bound.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/auth-context-items")]
public partial class CreateAuthContextItemCommand : ICommand<string>
{
    [Blueprint.Model.Attributes.FromAuthContext]
    public string? TenantId { get; set; }

    public string? Name { get; set; }

    public static Task<string> HandleAsync(CreateAuthContextItemCommand operation)
    {
        return Task.FromResult($"tenant={operation.TenantId ?? "null"},name={operation.Name}");
    }
}

/// <summary>
/// GET query with <c>[FromAuthContext]</c> on TenantId — proves the property is excluded
/// from query string binding and not visible in OpenAPI query parameters.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/auth-context-list")]
public partial class ListAuthContextItemsQuery : IQuery<string>
{
    [Blueprint.Model.Attributes.FromAuthContext]
    public string? TenantId { get; set; }

    public int Page { get; set; }

    public static Task<string> HandleAsync(ListAuthContextItemsQuery operation)
    {
        return Task.FromResult($"tenant={operation.TenantId ?? "null"},page={operation.Page}");
    }
}

/// <summary>
/// GET query where TenantId is BOTH <c>[FromAuthContext]</c> and a route placeholder —
/// proves route binding takes precedence and the property is bound from the URL.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/auth-context-tenants/{TenantId}/items/{Id}")]
public partial class GetAuthContextRouteItemQuery : IQuery<string>
{
    [Blueprint.Model.Attributes.FromAuthContext]
    public string TenantId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public static Task<string> HandleAsync(GetAuthContextRouteItemQuery operation)
    {
        return Task.FromResult($"tenant={operation.TenantId ?? "null"},id={operation.Id}");
    }
}

// --- Enum route/query binding test types ---

/// <summary>
/// Enum type for testing route and query parameter binding.
/// Mirrors S365 patterns where enums are used in routes (e.g. report category, status filters).
/// </summary>
public enum PopulationTestCategory
{
    Unknown = 0,
    Active = 1,
    Archived = 2,
    Deleted = 3,
}

/// <summary>
/// GET with enum route parameter — proves <c>System.Enum.TryParse</c> is generated
/// for enum types in route segments, matching S365's 100+ enum-in-route usages.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/categories/{Category}/items/{Id}")]
public partial class GetItemByEnumCategoryPopulationQuery : IQuery<string>
{
    public PopulationTestCategory Category { get; set; }

    public string Id { get; set; } = string.Empty;

    public static Task<string> HandleAsync(GetItemByEnumCategoryPopulationQuery operation)
    {
        return Task.FromResult($"{operation.Category}:{operation.Id}");
    }
}

/// <summary>
/// GET with nullable enum query parameter — proves nullable enum query binding
/// works at runtime (returns null when not provided, parses when present).
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/population-test/enum-search")]
public partial class SearchByEnumStatusPopulationQuery : IQuery<string>
{
    public PopulationTestCategory? Status { get; set; }

    public static Task<string> HandleAsync(SearchByEnumStatusPopulationQuery operation)
    {
        return Task.FromResult(operation.Status?.ToString() ?? "none");
    }
}

// --- FromAuthContext ClaimType test types ---

/// <summary>
/// GET query with <c>[FromAuthContext(ClaimType = "partner_id")]</c> on PartnerId — proves the
/// compile-time claim mapping reads the specified claim from the authenticated principal and
/// populates the property without runtime reflection or IPropertyPopulator.
/// </summary>
[Blueprint.Model.Attributes.MustBeAuthenticated]
[Blueprint.Model.Attributes.RootLink("/population-test/claim-mapped-items/{Id}")]
public partial class GetClaimMappedItemQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    [Blueprint.Model.Attributes.FromAuthContext(ClaimType = "partner_id")]
    public string? PartnerId { get; set; }

    public static Task<string> HandleAsync(GetClaimMappedItemQuery operation)
    {
        return Task.FromResult($"id={operation.Id},partner={operation.PartnerId ?? "null"}");
    }
}
