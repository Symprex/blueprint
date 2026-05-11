using System.Diagnostics;
using Blueprint;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Options;

// This is the recommendation from MS for extensions to IEndpointRouteBuilder to aid discoverability
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Adds Blueprint endpoint-mapping helpers to ASP.NET Core's endpoint-route builder for discoverable application startup.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    private static readonly HttpOperationResultExecutor DefaultOperationResultExecutor = new();

    /// <summary>
    /// Maps all Blueprint operation endpoints from the given <see cref="BlueprintAppExecutor" />.
    /// </summary>
    /// <typeparam name="T">The Blueprint app type whose operations should be exposed.</typeparam>
    /// <typeparam name="TExec">The generated executor type that can route those operations.</typeparam>
    /// <param name="endpoints">The endpoint builder to register with.</param>
    /// <param name="basePath">A base path to prepend to all routes.</param>
    /// <param name="configureOpenApi">
    /// An optional delegate that customises the OpenAPI <c>info</c> block (title, version, description).
    /// When <see langword="null"/> the defaults from <see cref="OpenApiInfoOptions"/> are used.
    /// </param>
    /// <param name="configureOpenApiSchemas">
    /// An optional delegate that registers ordered schema processors for the rendered OpenAPI <c>components/schemas</c>
    /// section. Processors run once when the document is pre-rendered at startup.
    /// </param>
    /// <param name="configureOpenApiOperations">
    /// An optional delegate that registers ordered per-operation processors for OpenAPI operation objects.
    /// Each processor can modify the rendered operation (e.g. removing auto-populated parameters like TenantId
    /// from the public spec). Processors run once when the document is pre-rendered at startup.
    /// </param>
    /// <returns>A builder that allows adding metadata / conventions to all mapped endpoints.</returns>
    public static IEndpointConventionBuilder MapBlueprintApi<T, TExec>(
        this IEndpointRouteBuilder endpoints,
        string basePath,
        Action<OpenApiInfoOptions>? configureOpenApi = null,
        Action<OpenApiSchemaProcessorOptions>? configureOpenApiSchemas = null,
        Action<OpenApiOperationProcessorOptions>? configureOpenApiOperations = null) where T : BlueprintApp, new()
                         where TExec : BlueprintAppExecutor
    {
        // Ensure ends with a slash, but only one
        basePath = basePath.TrimEnd('/') + '/';

        var runtimeModel = new T();

        var httpOptions = endpoints.ServiceProvider.GetService<IOptions<BlueprintHttpOptions>>()?.Value ?? new BlueprintHttpOptions();

        RequestDelegate requestDelegate = new BlueprintApiRouter<TExec>(
            endpoints.ServiceProvider,
            runtimeModel,
            endpoints.ServiceProvider.GetRequiredService<ILogger<BlueprintApiRouter<TExec>>>(),
            httpOptions,
            basePath).RouteAsync;

        var builders = new List<IEndpointConventionBuilder>();

        // OpenAPI is a required part of the HTTP host and is always exposed regardless of environment.
        OpenApiInfoOptions? infoOptions = null;
        if (configureOpenApi is not null)
        {
            infoOptions = new OpenApiInfoOptions();
            configureOpenApi(infoOptions);
        }

        OpenApiSchemaProcessorOptions? schemaProcessorOptions = null;
        if (configureOpenApiSchemas is not null)
        {
            schemaProcessorOptions = new OpenApiSchemaProcessorOptions();
            configureOpenApiSchemas(schemaProcessorOptions);
        }

        OpenApiOperationProcessorOptions? operationProcessorOptions = null;
        if (configureOpenApiOperations is not null)
        {
            operationProcessorOptions = new OpenApiOperationProcessorOptions();
            configureOpenApiOperations(operationProcessorOptions);
        }

        var (openApiDelegate, openApiDocument) = OpenApiDocumentEndpoint.Create(runtimeModel, infoOptions, schemaProcessorOptions, operationProcessorOptions);

        // Register the pre-rendered document in DI so development utilities (e.g. OutputOpenApi) can retrieve it
        endpoints.ServiceProvider.GetService<PreRenderedOpenApiDocumentHolder>()?.Set(openApiDocument);

        var openApiBuilder = endpoints.Map(
            RoutePatternFactory.Parse(basePath.TrimEnd('/') + "/.blueprint/openapi.json"),
            openApiDelegate);
        openApiBuilder.WithDisplayName("GET Blueprint OpenAPI document");
        openApiBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

        builders.Add(openApiBuilder);

        foreach (var operation in runtimeModel.Operations)
        {
            // Only map operations that are exposed to the HTTP host (have URLs).
            if (operation.Urls.Count == 0)
            {
                continue;
            }

            // Resolve HTTP methods from compile-time feature data, falling back to GET for safety.
            var httpMethods = operation.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methodData)
                ? methodData.HttpMethods.ToArray()
                : ["GET"];

            foreach (var url in operation.Urls)
            {
                var builder = endpoints.Map(RoutePatternFactory.Parse(basePath.TrimEnd('/') + url.UrlFormat), requestDelegate);
                builder.WithMetadata(operation);
                builder.WithMetadata(new HttpMethodMetadata(httpMethods));
                builder.WithDisplayName(string.Concat(string.Join('|', httpMethods), " ", operation.Name));

                builders.Add(builder);
            }
        }

        if (endpoints.ServiceProvider.GetService<IWebHostEnvironment>()?.IsDevelopment() == true)
        {
            var manifestInspectionBasePath = basePath.TrimEnd('/') + "/.blueprint/manifests/operations";

            if (!string.IsNullOrWhiteSpace(httpOptions.OperationsManifestResourceName))
            {
                var manifestBuilder = endpoints.Map(
                    RoutePatternFactory.Parse(manifestInspectionBasePath + ".json"),
                    ManifestJsonInspectionEndpoint.Create(typeof(T).Assembly, httpOptions.OperationsManifestResourceName));
                manifestBuilder.WithDisplayName("GET Blueprint manifest operations JSON");
                manifestBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

                builders.Add(manifestBuilder);

                var manifestPageBuilder = endpoints.Map(
                    RoutePatternFactory.Parse(manifestInspectionBasePath + ".html"),
                    ManifestDiagnosticsPageEndpoint.Create());
                manifestPageBuilder.WithDisplayName("GET Blueprint manifest operations diagnostics page");
                manifestPageBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

                builders.Add(manifestPageBuilder);
            }

            if (!string.IsNullOrWhiteSpace(httpOptions.OperationsManifestMarkdownResourceName))
            {
                var manifestMarkdownBuilder = endpoints.Map(
                    RoutePatternFactory.Parse(manifestInspectionBasePath + ".md"),
                    ManifestMarkdownInspectionEndpoint.Create(typeof(T).Assembly, httpOptions.OperationsManifestMarkdownResourceName));
                manifestMarkdownBuilder.WithDisplayName("GET Blueprint manifest operations markdown");
                manifestMarkdownBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

                builders.Add(manifestMarkdownBuilder);
            }

            if (!string.IsNullOrWhiteSpace(httpOptions.OperationsManifestSchemaResourceName))
            {
                var manifestSchemaBuilder = endpoints.Map(
                    RoutePatternFactory.Parse(manifestInspectionBasePath + ".schema.json"),
                    ManifestSchemaInspectionEndpoint.Create(typeof(T).Assembly, httpOptions.OperationsManifestSchemaResourceName));
                manifestSchemaBuilder.WithDisplayName("GET Blueprint manifest operations schema");
                manifestSchemaBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

                builders.Add(manifestSchemaBuilder);
            }
        }

        return new BlueprintEndpointRouteBuilder(builders);
    }

    private class BlueprintApiRouter<TExec> where TExec : BlueprintAppExecutor
    {
        private readonly IServiceProvider _rootServiceProvider;
        private readonly BlueprintApp _app;
        private readonly ILogger<BlueprintApiRouter<TExec>> _logger;
        private readonly BlueprintHttpOptions _httpOptions;
        private readonly PathString _normalizedBasePath;
        private readonly TExec _executor;
        private readonly HttpOperationResultExecutor? _resultExecutor;

        // Cached base URI to avoid per-request string interpolation when scheme/host/path are stable
        private string? _cachedBaseUri;
        private string? _cachedScheme;
        private string? _cachedHost;
        private PathString _cachedPathBase;

        public BlueprintApiRouter(
            IServiceProvider rootServiceProvider,
            BlueprintApp app,
            ILogger<BlueprintApiRouter<TExec>> logger,
            BlueprintHttpOptions httpOptions,
            string basePath)
        {
            this._rootServiceProvider = rootServiceProvider;
            this._app = app;
            this._logger = logger;
            this._httpOptions = httpOptions;
            this._normalizedBasePath = new PathString("/" + basePath.TrimStart('/'));

            // Cache singleton services that are resolved on every request
            this._executor = rootServiceProvider.GetRequiredService<TExec>();
            this._resultExecutor = rootServiceProvider.GetService<HttpOperationResultExecutor>();
        }

        public async Task RouteAsync(HttpContext httpContext)
        {
            var endpoint = httpContext.GetEndpoint()!;
            var httpRequest = httpContext.Request;

            var operation = endpoint.Metadata.GetMetadata<ApiOperationDescriptor>()!;

            // Set the activity display name to the operation type for better trace visibility
            var activity = Activity.Current;
            if (activity != null)
            {
                activity.DisplayName = operation.Name;
            }

            // Method enforcement is handled by ASP.NET Core routing via HttpMethodMetadata on mapped endpoints.

            using var nestedContainer = this._rootServiceProvider.CreateScope();

            var apiContext = new ApiOperationContext(
                nestedContainer.ServiceProvider,
                this._app,
                operation,
                httpContext.RequestAborted)
            {
                HttpContext = httpContext,
                UserPrincipal = httpContext.User,
            };

            // Compute a stable base URI for link generation and redirect targets.
            // Cache the result to avoid per-request string interpolation when values are stable.
            var host = this._httpOptions.PublicHost ?? httpRequest.Host.Value;
            var basePath = httpRequest.PathBase.Add(this._normalizedBasePath);
            var scheme = httpRequest.Scheme;

            if (this._cachedBaseUri is null
                || !string.Equals(scheme, this._cachedScheme, StringComparison.Ordinal)
                || !string.Equals(host, this._cachedHost, StringComparison.Ordinal)
                || basePath != this._cachedPathBase)
            {
                this._cachedScheme = scheme;
                this._cachedHost = host;
                this._cachedPathBase = basePath;
                this._cachedBaseUri = $"{scheme}://{host}{basePath}";
            }

            apiContext.BaseUri = this._cachedBaseUri;

            try
            {
                var result = await this._executor.GetPipeline(operation.OperationType).ExecuteAsync(apiContext).ConfigureAwait(false);

                var operationResultExecutor = this._resultExecutor ?? DefaultOperationResultExecutor;
                if (await operationResultExecutor.TryExecuteAsync(httpContext, apiContext, result, this._httpOptions).ConfigureAwait(false) == false)
                {
                    // Legacy self-executing results still flow through the old path until the broader host-translation workstream lands.
                    await result.ExecuteAsync(apiContext).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected — no response needed. Let ASP.NET Core handle the abort.
                throw;
            }
            catch (Exception ex)
            {
                // Defense-in-depth: the generated pipeline has its own exception translation, but if something
                // truly unexpected escapes (DI failure, result executor crash), emit a ProblemDetails 500.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                this._logger.LogError(ex, "Unhandled exception escaped pipeline for operation {OperationName}", operation.Name);

                if (!httpContext.Response.HasStarted)
                {
                    httpContext.Response.StatusCode = 500;
                    httpContext.Response.ContentType = "application/problem+json";

                    var exposeDetails = this._httpOptions.ExposeExceptionDetailsInErrorResponses;
                    var detail = exposeDetails ? ex.ToString() : "An unexpected error occurred.";

                    await httpContext.Response.WriteAsync(
                        $$"""{"type":"https://httpstatuses.com/500","title":"Internal Server Error","status":500,"detail":"{{EscapeJsonString(detail)}}"}""",
                        httpContext.RequestAborted).ConfigureAwait(false);
                }
            }
        }

        private static string EscapeJsonString(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
        }
    }
}
