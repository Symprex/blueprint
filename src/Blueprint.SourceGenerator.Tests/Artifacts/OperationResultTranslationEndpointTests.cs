using System.Text;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class OperationResultTranslationEndpointTests
{
    [Fact]
    public async Task When_http_endpoint_pipeline_returns_ok_result_then_host_route_translates_it_without_self_executing_result_call()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/ok-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Be("\"Blueprint\"");
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_created_result_then_host_route_completes_with_201_json_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/created-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Be("\"Blueprint\"");
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_no_result_then_host_route_completes_with_empty_200_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/no-result-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().BeNull();
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_http_owned_no_content_result_then_host_route_completes_with_empty_204_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/http-no-content-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        response.ContentType.Should().BeNull();
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_key_only_resource_event_result_then_host_route_completes_with_empty_201_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/key-only-resource-event-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.ContentType.Should().BeNull();
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_unsupported_result_then_route_falls_back_to_legacy_self_executing_contract()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/legacy-operation");

        endpoint.Should().NotBeNull();
        LegacySelfExecutingResult.Reset();

        _ = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        LegacySelfExecutingResult.ExecuteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_ok_result_with_null_content_then_host_route_fails_fast()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/null-ok-operation");

        endpoint.Should().NotBeNull();

        var act = () => InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OkResult*null content*");
    }

    private static async Task<TestEndpointInvocationResult> InvokeEndpointAsync(RouteEndpoint endpoint, IServiceProvider serviceProvider)
    {
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            Response =
            {
                Body = body,
            },
        };

        context.SetEndpoint(endpoint);
        var requestPath = endpoint.RoutePattern.RawText ?? string.Empty;
        context.Request.Path = "/" + requestPath.TrimStart('/');

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        return new TestEndpointInvocationResult(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static RouteEndpoint? FindEndpoint(TestEndpointRouteBuilder endpoints, string routePattern)
    {
        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(endpoint => string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal));
    }

    private static TestEndpointRouteBuilder CreateEndpoints()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ResultTranslationBlueprintAppExecutor>()
            .BuildServiceProvider();

        return new TestEndpointRouteBuilder(services);
    }

    private sealed class ResultTranslationBlueprintApp : BlueprintApp
    {
        public ResultTranslationBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(ResultTranslationOperation), "ResultTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            descriptor.AddUrl(new ApiOperationUrl("/ok-operation"));
            this.Register(descriptor);

            var createdDescriptor = new ApiOperationDescriptor(typeof(CreatedTranslationOperation), "CreatedTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            createdDescriptor.AddUrl(new ApiOperationUrl("/created-operation"));
            this.Register(createdDescriptor);

            var noResultDescriptor = new ApiOperationDescriptor(typeof(NoResultTranslationOperation), "NoResultTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            noResultDescriptor.AddUrl(new ApiOperationUrl("/no-result-operation"));
            this.Register(noResultDescriptor);

            var httpNoContentDescriptor = new ApiOperationDescriptor(typeof(HttpNoContentTranslationOperation), "HttpNoContentTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            httpNoContentDescriptor.AddUrl(new ApiOperationUrl("/http-no-content-operation"));
            this.Register(httpNoContentDescriptor);

            var keyOnlyResourceEventDescriptor = new ApiOperationDescriptor(typeof(KeyOnlyResourceEventTranslationOperation), "KeyOnlyResourceEventTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            keyOnlyResourceEventDescriptor.AddUrl(new ApiOperationUrl("/key-only-resource-event-operation"));
            this.Register(keyOnlyResourceEventDescriptor);

            var legacyDescriptor = new ApiOperationDescriptor(typeof(LegacyFallbackOperation), "LegacyFallbackOperation", nameof(OperationResultTranslationEndpointTests));
            legacyDescriptor.AddUrl(new ApiOperationUrl("/legacy-operation"));
            this.Register(legacyDescriptor);

            var nullOkDescriptor = new ApiOperationDescriptor(typeof(NullOkResultOperation), "NullOkResultOperation", nameof(OperationResultTranslationEndpointTests));
            nullOkDescriptor.AddUrl(new ApiOperationUrl("/null-ok-operation"));
            this.Register(nullOkDescriptor);
        }
    }

    private sealed class ResultTranslationBlueprintAppExecutor : BlueprintAppExecutor
    {
        public ResultTranslationBlueprintAppExecutor()
            : base(new ResultTranslationBlueprintApp())
        {
            this.Register(typeof(ResultTranslationOperation), new SuccessfulResultPipeline());
            this.Register(typeof(CreatedTranslationOperation), new CreatedResultPipeline());
            this.Register(typeof(NoResultTranslationOperation), new NoResultPipeline());
            this.Register(typeof(HttpNoContentTranslationOperation), new HttpNoContentPipeline());
            this.Register(typeof(KeyOnlyResourceEventTranslationOperation), new KeyOnlyResourceEventPipeline());
            this.Register(typeof(LegacyFallbackOperation), new LegacyFallbackPipeline());
            this.Register(typeof(NullOkResultOperation), new NullOkResultPipeline());
        }
    }

    private sealed class SuccessfulResultPipeline : IOperationExecutorPipeline
    {
        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return Task.FromResult<OperationResult>(new OkResult<string>("Blueprint"));
        }
    }

    private sealed class CreatedResultPipeline : IOperationExecutorPipeline
    {
        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return Task.FromResult<OperationResult>(new CreatedResult<string>("Blueprint"));
        }
    }

    private sealed class NoResultPipeline : IOperationExecutorPipeline
    {
        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return Task.FromResult<OperationResult>(new NoResultOperationResult());
        }
    }

    private sealed class HttpNoContentPipeline : IOperationExecutorPipeline
    {
        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return Task.FromResult<OperationResult>(new HttpNoContentResult());
        }
    }

    private sealed class KeyOnlyResourceEventPipeline : IOperationExecutorPipeline
    {
        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return Task.FromResult<OperationResult>(new KeyOnlyResourceEventResult("events.deleted", "resource-123"));
        }
    }

    private sealed class LegacyFallbackPipeline : IOperationExecutorPipeline
    {
        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return Task.FromResult<OperationResult>(new LegacySelfExecutingResult());
        }
    }

    private sealed class NullOkResultPipeline : IOperationExecutorPipeline
    {
        public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return Task.FromResult<OperationResult>(new OkResult(null!));
        }
    }

    private sealed class ResultTranslationOperation;

    private sealed class CreatedTranslationOperation;

    private sealed class NoResultTranslationOperation;

    private sealed class HttpNoContentTranslationOperation;

    private sealed class KeyOnlyResourceEventTranslationOperation;

    private sealed class LegacyFallbackOperation;

    private sealed class NullOkResultOperation;

    private sealed class LegacySelfExecutingResult : OperationResult
    {
        public static int ExecuteCallCount { get; private set; }

        public static void Reset()
        {
            ExecuteCallCount = 0;
        }

        public override Task ExecuteAsync(ApiOperationContext context)
        {
            ExecuteCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }

    private sealed record TestEndpointInvocationResult(
        int StatusCode,
        string? ContentType,
        string Body);
}
