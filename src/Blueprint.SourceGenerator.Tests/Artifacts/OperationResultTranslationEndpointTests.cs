using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public partial class OperationResultTranslationEndpointTests
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
    public async Task When_http_endpoint_pipeline_returns_accepted_result_then_host_route_completes_with_202_json_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/accepted-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Be("\"Blueprint\"");
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_conflict_result_then_host_route_completes_with_409_json_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/conflict-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Be("\"Blueprint\"");
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_not_found_result_then_host_route_completes_with_404_json_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/not-found-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Be("\"Blueprint\"");
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_object_result_and_app_declares_serializer_context_then_host_route_uses_the_app_owned_system_text_json_contract()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/object-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Be("{\"displayName\":\"Blueprint\",\"totalCount\":3}");
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
    public async Task When_http_endpoint_pipeline_returns_key_only_resource_event_result_then_host_route_completes_with_204_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/key-only-resource-event-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        response.ContentType.Should().BeNull();
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_body_bearing_resource_event_result_then_host_route_completes_with_201_json_envelope()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/body-resource-event-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.ContentType.Should().Be("application/json; charset=utf-8");

        var body = JsonDocument.Parse(response.Body).RootElement;
        body.GetProperty("$object").GetString().Should().Be("event");
        body.GetProperty("eventId").GetString().Should().Be("events.created");
        body.GetProperty("changeType").GetString().Should().Be("Created");
        body.GetProperty("created").GetString().Should().NotBeNullOrEmpty();

        // Legacy removes "DTO" case-sensitively (Blueprint.Http/ApiResource.cs:68-77), so a "Dto" is kept.
        body.GetProperty("resourceObject").GetString().Should().Be("resultTranslationResponseDto");
        body.GetProperty("data").GetProperty("displayName").GetString().Should().Be("Blueprint");
        body.GetProperty("data").GetProperty("totalCount").GetInt32().Should().Be(3);
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
    public async Task When_http_endpoint_pipeline_returns_validation_failed_result_with_matching_failure_metadata_then_host_route_completes_with_422_problem_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/validation-failed-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().Be("application/problem+json; charset=utf-8");
        response.Body.Should().Contain("\"status\":422")
            .And.Contain("Name is required")
            .And.Contain("Name");
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_validation_failed_result_without_matching_failure_metadata_then_route_falls_back_to_legacy_self_executing_contract()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/validation-failed-without-metadata-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().BeNull();
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_unhandled_exception_result_with_matching_failure_metadata_then_host_route_completes_with_500_problem_response()
    {
        var endpoints = CreateEndpoints(static services =>
            services.AddSingleton<IOptions<BlueprintHttpOptions>>(Options.Create(new BlueprintHttpOptions
            {
                ExposeExceptionDetailsInErrorResponses = true,
            })));
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/unhandled-exception-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.ContentType.Should().Be("application/problem+json; charset=utf-8");
        response.Body.Should().Contain("\"status\":500")
            .And.Contain("Unhandled failure");
    }

    [Fact]
    public async Task When_http_endpoint_pipeline_returns_ok_result_with_null_content_then_host_route_completes_with_500_problem_response()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<ResultTranslationBlueprintApp, ResultTranslationBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/null-ok-operation");

        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.ContentType.Should().Be("application/problem+json");

        // Legacy wrote an exception its result writing threw as any other (Signature365 8b40ecc961,
        // Blueprint.Http/OkResultOperationExecutor.cs:48-65, UnhandledExceptionOperationResultExecutor.cs:60-67).
        response.Body.Should().Be("""{"type":"unknown_error","title":"Something has gone wrong, please try again","status":500}""");
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

    private static TestEndpointRouteBuilder CreateEndpoints(Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ResultTranslationBlueprintAppExecutor>();

        configureServices?.Invoke(services);

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private sealed class ResultTranslationBlueprintApp : BlueprintApp
    {
        public ResultTranslationBlueprintApp()
        {
            this.AppOwnedSerializerContext = new ResultTranslationJsonContext();
            this.AppOwnedSerializerContextType = typeof(ResultTranslationJsonContext);
            this.AppOwnedSerializerContextBodyTypes =
            [
                typeof(string),
                typeof(ResultTranslationResponseDto),
                typeof(Microsoft.AspNetCore.Mvc.ProblemDetails),
                typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails),
            ];
            this.AppOwnedSerializerContextBodyTypeNames =
            [
                typeof(string).FullName!,
                typeof(ResultTranslationResponseDto).FullName!,
                typeof(Microsoft.AspNetCore.Mvc.ProblemDetails).FullName!,
                typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails).FullName!,
            ];

            var descriptor = new ApiOperationDescriptor(typeof(ResultTranslationOperation), "ResultTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            descriptor.AddUrl(new ApiOperationUrl("/ok-operation"));
            this.Register(descriptor);

            var createdDescriptor = new ApiOperationDescriptor(typeof(CreatedTranslationOperation), "CreatedTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            createdDescriptor.AddUrl(new ApiOperationUrl("/created-operation"));
            this.Register(createdDescriptor);

            var acceptedDescriptor = new ApiOperationDescriptor(typeof(AcceptedTranslationOperation), "AcceptedTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            acceptedDescriptor.AddUrl(new ApiOperationUrl("/accepted-operation"));
            this.Register(acceptedDescriptor);

            var conflictDescriptor = new ApiOperationDescriptor(typeof(ConflictTranslationOperation), "ConflictTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            conflictDescriptor.AddUrl(new ApiOperationUrl("/conflict-operation"));
            this.Register(conflictDescriptor);

            var notFoundDescriptor = new ApiOperationDescriptor(typeof(NotFoundTranslationOperation), "NotFoundTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            notFoundDescriptor.AddUrl(new ApiOperationUrl("/not-found-operation"));
            this.Register(notFoundDescriptor);

            var objectDescriptor = new ApiOperationDescriptor(typeof(ObjectTranslationOperation), "ObjectTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            objectDescriptor.AddUrl(new ApiOperationUrl("/object-operation"));
            this.Register(objectDescriptor);

            var noResultDescriptor = new ApiOperationDescriptor(typeof(NoResultTranslationOperation), "NoResultTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            noResultDescriptor.AddUrl(new ApiOperationUrl("/no-result-operation"));
            this.Register(noResultDescriptor);

            var httpNoContentDescriptor = new ApiOperationDescriptor(typeof(HttpNoContentTranslationOperation), "HttpNoContentTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            httpNoContentDescriptor.AddUrl(new ApiOperationUrl("/http-no-content-operation"));
            this.Register(httpNoContentDescriptor);

            var keyOnlyResourceEventDescriptor = new ApiOperationDescriptor(typeof(KeyOnlyResourceEventTranslationOperation), "KeyOnlyResourceEventTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            keyOnlyResourceEventDescriptor.AddUrl(new ApiOperationUrl("/key-only-resource-event-operation"));
            this.Register(keyOnlyResourceEventDescriptor);

            var bodyResourceEventDescriptor = new ApiOperationDescriptor(typeof(BodyResourceEventTranslationOperation), "BodyResourceEventTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            bodyResourceEventDescriptor.AddUrl(new ApiOperationUrl("/body-resource-event-operation"));
            this.Register(bodyResourceEventDescriptor);

            var legacyDescriptor = new ApiOperationDescriptor(typeof(LegacyFallbackOperation), "LegacyFallbackOperation", nameof(OperationResultTranslationEndpointTests));
            legacyDescriptor.AddUrl(new ApiOperationUrl("/legacy-operation"));
            this.Register(legacyDescriptor);

            var validationFailedDescriptor = new ApiOperationDescriptor(typeof(ValidationFailedTranslationOperation), "ValidationFailedTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            validationFailedDescriptor.AddUrl(new ApiOperationUrl("/validation-failed-operation"));
            validationFailedDescriptor.SetFeatureData(CreateFailureOutcomes(
                new OperationFailureOutcome("data-annotations-validation-exception", "System.ComponentModel.DataAnnotations.ValidationException", "Blueprint.Runtime.Exec.ValidationFailedResult")));
            this.Register(validationFailedDescriptor);

            var validationFailedWithoutMetadataDescriptor = new ApiOperationDescriptor(typeof(ValidationFailedWithoutMetadataOperation), "ValidationFailedWithoutMetadataOperation", nameof(OperationResultTranslationEndpointTests));
            validationFailedWithoutMetadataDescriptor.AddUrl(new ApiOperationUrl("/validation-failed-without-metadata-operation"));
            this.Register(validationFailedWithoutMetadataDescriptor);

            var unhandledExceptionDescriptor = new ApiOperationDescriptor(typeof(UnhandledExceptionTranslationOperation), "UnhandledExceptionTranslationOperation", nameof(OperationResultTranslationEndpointTests));
            unhandledExceptionDescriptor.AddUrl(new ApiOperationUrl("/unhandled-exception-operation"));
            unhandledExceptionDescriptor.SetFeatureData(CreateFailureOutcomes(
                new OperationFailureOutcome("default-exception", "System.Exception", "Blueprint.Runtime.Exec.UnhandledExceptionResult")));
            this.Register(unhandledExceptionDescriptor);

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
            this.Register(typeof(AcceptedTranslationOperation), new AcceptedResultPipeline());
            this.Register(typeof(ConflictTranslationOperation), new ConflictResultPipeline());
            this.Register(typeof(NotFoundTranslationOperation), new NotFoundResultPipeline());
            this.Register(typeof(ObjectTranslationOperation), new ObjectResultPipeline());
            this.Register(typeof(NoResultTranslationOperation), new NoResultPipeline());
            this.Register(typeof(HttpNoContentTranslationOperation), new HttpNoContentPipeline());
            this.Register(typeof(KeyOnlyResourceEventTranslationOperation), new KeyOnlyResourceEventPipeline());
            this.Register(typeof(BodyResourceEventTranslationOperation), new BodyResourceEventPipeline());
            this.Register(typeof(LegacyFallbackOperation), new LegacyFallbackPipeline());
            this.Register(typeof(ValidationFailedTranslationOperation), new ValidationFailedResultPipeline());
            this.Register(typeof(ValidationFailedWithoutMetadataOperation), new ValidationFailedWithoutMetadataPipeline());
            this.Register(typeof(UnhandledExceptionTranslationOperation), new UnhandledExceptionResultPipeline());
            this.Register(typeof(NullOkResultOperation), new NullOkResultPipeline());
        }
    }

    private sealed class SuccessfulResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new OkResult<string>("Blueprint"));
        }
    }

    private sealed class CreatedResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new CreatedResult<string>("Blueprint"));
        }
    }

    private sealed class AcceptedResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new AcceptedResult<string>("Blueprint"));
        }
    }

    private sealed class ConflictResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new ConflictResult<string>("Blueprint"));
        }
    }

    private sealed class NoResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(NoResultOperationResult.Instance);
        }
    }

    private sealed class ObjectResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new OkResult<ResultTranslationResponseDto>(new ResultTranslationResponseDto
            {
                DisplayName = "Blueprint",
                TotalCount = 3,
            }));
        }
    }

    private sealed class HttpNoContentPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new HttpNoContentResult());
        }
    }

    private sealed class KeyOnlyResourceEventPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new KeyOnlyResourceEventResult(ChangeType.Deleted, "events.deleted", "resource-123"));
        }
    }

    private sealed class BodyResourceEventPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new ResourceEventResult<ResultTranslationResponseDto>(
                ChangeType.Created,
                "events.created",
                "resource-123",
                new ResultTranslationResponseDto
                {
                    DisplayName = "Blueprint",
                    TotalCount = 3,
                }));
        }
    }

    private sealed class NotFoundResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new NotFoundResult<string>("Blueprint"));
        }
    }

    private sealed class LegacyFallbackPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new LegacySelfExecutingResult());
        }
    }

    private sealed class ValidationFailedResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new ValidationFailedResult(
            [
                new ValidationResult("Name is required", ["Name"]),
            ]));
        }
    }

    private sealed class ValidationFailedWithoutMetadataPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new ValidationFailedResult(
            [
                new ValidationResult("Name is required", ["Name"]),
            ]));
        }
    }

    private sealed class UnhandledExceptionResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new UnhandledExceptionResult(new InvalidOperationException("Unhandled failure")));
        }
    }

    private sealed class NullOkResultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new OkResult(null!));
        }
    }

    private sealed class ResultTranslationOperation;

    private sealed class CreatedTranslationOperation;

    private sealed class AcceptedTranslationOperation;

    private sealed class ConflictTranslationOperation;

    private sealed class NotFoundTranslationOperation;

    private sealed class ObjectTranslationOperation;

    private sealed class NoResultTranslationOperation;

    private sealed class HttpNoContentTranslationOperation;

    private sealed class KeyOnlyResourceEventTranslationOperation;

    private sealed class BodyResourceEventTranslationOperation;

    private sealed class LegacyFallbackOperation;

    private sealed class ValidationFailedTranslationOperation;

    private sealed class ValidationFailedWithoutMetadataOperation;

    private sealed class UnhandledExceptionTranslationOperation;

    private sealed class NullOkResultOperation;

    public sealed class ResultTranslationResponseDto
    {
        [JsonPropertyName("displayName")]
        public required string DisplayName { get; init; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; init; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(ResultTranslationResponseDto))]
    [JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
    [JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails))]
    public partial class ResultTranslationJsonContext : JsonSerializerContext;

    private static OperationFailureOutcomesFeatureData CreateFailureOutcomes(params OperationFailureOutcome[] outcomes)
    {
        return new OperationFailureOutcomesFeatureData(ImmutableArray.Create(outcomes));
    }

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
