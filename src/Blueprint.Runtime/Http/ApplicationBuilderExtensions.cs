using System.Collections.Immutable;
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
        // Ensure ends with a slash, but only one, as legacy did (Signature365 8b40ecc961,
        // Blueprint.Http/ApplicationBuilderExtensions.cs:45). Every route is this followed by a URL without its leading slash
        // (RouteUnder), as legacy appended its leading-slash-trimmed link URLs (:105; Blueprint/ApiOperationLink.cs:48), and
        // every absolute path is that with a leading slash: "", "/" -> "/"; "api", "api/", "/api/" -> "/api/".
        basePath = basePath.TrimEnd('/') + '/';
        var absoluteBasePath = "/" + basePath.TrimStart('/');

        // Prefer the DI-registered singleton (set up by AddBlueprintApi with serializer options applied).
        // Fall back to direct construction for testing scenarios that build a manual service collection.
        var runtimeModel = endpoints.ServiceProvider.GetService<T>() ?? new T();

        var httpOptions = endpoints.ServiceProvider.GetService<IOptions<BlueprintHttpOptions>>()?.Value ?? new BlueprintHttpOptions();

        var router = new BlueprintApiRouter<TExec>(
            endpoints.ServiceProvider,
            runtimeModel,
            endpoints.ServiceProvider.GetRequiredService<ILogger<BlueprintApiRouter<TExec>>>(),
            httpOptions,
            absoluteBasePath);

        // Pre-size based on expected operations (each may have 1-2 URLs plus fixed well-known endpoints).
        var builders = new List<IEndpointConventionBuilder>((runtimeModel.OperationCount * 2) + 8);

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

        var (openApiDelegate, openApiDocument) = OpenApiDocumentEndpoint.Create(runtimeModel, infoOptions, schemaProcessorOptions, operationProcessorOptions, httpOptions, basePath);

        // Register the pre-rendered document in DI so development utilities (e.g. OutputOpenApi) can retrieve it
        endpoints.ServiceProvider.GetService<PreRenderedOpenApiDocumentHolder>()?.Set(openApiDocument);

        // What the document could not describe is named once, when the API is mapped, for the consumer to act on.
        if (openApiDocument.Warnings.Count > 0
            && endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Blueprint.OpenApi") is { } openApiLogger)
        {
            foreach (var warning in openApiDocument.Warnings)
            {
                openApiLogger.LogWarning("{OpenApiWarning}", warning);
            }
        }

        var openApiBuilder = endpoints.Map(
            RoutePatternFactory.Parse(RouteUnder(basePath, ".blueprint/openapi.json")),
            openApiDelegate);
        openApiBuilder.WithDisplayName("GET Blueprint OpenAPI document");
        openApiBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

        builders.Add(openApiBuilder);

        // Serves the same pre-rendered document at a well-known location so tooling
        // can auto-discover the spec without knowing the Blueprint-specific base path.
        var wellKnownBuilder = endpoints.Map(
            RoutePatternFactory.Parse("/.well-known/openapi"),
            openApiDelegate);
        wellKnownBuilder.WithDisplayName("GET OpenAPI document (well-known)");
        wellKnownBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

        builders.Add(wellKnownBuilder);

        // RFC 9727 (June 2025) — api-catalog well-known endpoint for API discovery.
        // Returns a Linkset JSON document (application/linkset+json) pointing to the
        // OpenAPI spec, enabling standardised machine-readable API catalogue discovery.
        var openApiPath = absoluteBasePath + ".blueprint/openapi.json";
        var apiCatalogTitle = infoOptions?.Title ?? "Blueprint API";
        var apiCatalogBuilder = endpoints.Map(
            RoutePatternFactory.Parse("/.well-known/api-catalog"),
            CreateApiCatalogDelegate(openApiPath, apiCatalogTitle));
        apiCatalogBuilder.WithDisplayName("GET API catalog (RFC 9727)");
        apiCatalogBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));

        builders.Add(apiCatalogBuilder);

        // Track mapped (route, operationType) pairs to deduplicate. Multiple HATEOAS links per
        // operation are valid, but each unique route+operation pair should produce only one ASP.NET
        // Core endpoint to avoid ambiguous routing.
        var mappedEndpoints = new HashSet<(string Route, Type OperationType)>(runtimeModel.OperationCount * 2, StringTupleComparer.Instance);

        // Track route→operation for cross-operation collision detection.
        // Key: (normalised route, HTTP method); Value: first operation name that claimed this route.
        var routeOwners = new Dictionary<(string Route, string Method), string>(runtimeModel.OperationCount * 2, RouteMethodComparer.Instance);

        var logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Blueprint.Routing");

        // Whether one of the app's own operations answers GET at the base path, where the root-metadata document goes.
        var baseRouteClaimedBy = default(string);

        foreach (var operation in runtimeModel.Operations)
        {
            // Only map operations that are exposed to the HTTP host (have URLs).
            if (operation.Urls.Count == 0)
            {
                continue;
            }

            // Resolve HTTP methods from compile-time feature data, falling back to GET for safety.
            var httpMethods = operation.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methodData)
                ? methodData.HttpMethodsArray
                : ["GET"];

            foreach (var url in operation.Urls)
            {
                // Skip duplicate registrations for the same route+operation combination.
                if (!mappedEndpoints.Add((url.UrlFormat, operation.OperationType)))
                {
                    continue;
                }

                // Detect cross-operation route collisions: different operations claiming the same route+method.
                foreach (var method in httpMethods)
                {
                    var routeKey = (url.UrlFormat, method);

                    if (routeOwners.TryGetValue(routeKey, out var existingOwner))
                    {
                        if (!string.Equals(existingOwner, operation.Name, StringComparison.Ordinal))
                        {
                            logger?.LogWarning(
                                "Route collision detected: {Method} {Route} is claimed by both '{ExistingOperation}' and '{NewOperation}'. " +
                                "The first registration wins at runtime. Review [Link]/[SelfLink] attributes to resolve.",
                                method,
                                url.UrlFormat,
                                existingOwner,
                                operation.Name);
                        }
                    }
                    else
                    {
                        routeOwners[routeKey] = operation.Name;
                    }
                }

                if (url.UrlFormat.Trim('/').Length == 0 && httpMethods.Contains("GET", StringComparer.OrdinalIgnoreCase))
                {
                    baseRouteClaimedBy ??= operation.Name;
                }

                var builder = endpoints.Map(RoutePatternFactory.Parse(RouteUnder(basePath, url.UrlFormat)), router.CreateRequestDelegate(operation));
                builder.WithMetadata(operation);
                builder.WithMetadata(new HttpMethodMetadata(httpMethods));
                builder.WithDisplayName(string.Concat(string.Join('|', httpMethods), " ", operation.Name));

                builders.Add(builder);
            }
        }

        // Legacy answered GET at every HTTP app's base path with its root-metadata document (RootMetadataDocument). An app
        // that routes an operation of its own there keeps it: legacy would have mapped the two to one route, ambiguously.
        if (baseRouteClaimedBy is null)
        {
            var rootBuilder = endpoints.Map(
                RoutePatternFactory.Parse(basePath),
                router.CreateRootMetadataDocumentDelegate(RootMetadataDocument.CollectRootLinks(runtimeModel)));
            rootBuilder.WithDisplayName("GET Blueprint root metadata document");
            rootBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

            builders.Add(rootBuilder);
        }
        else
        {
            logger?.LogInformation(
                "The Blueprint root metadata document is not mapped at {BasePath}, because the operation '{Operation}' answers GET there.",
                absoluteBasePath,
                baseRouteClaimedBy);
        }

        if (endpoints.ServiceProvider.GetService<IWebHostEnvironment>()?.IsDevelopment() == true)
        {
            var manifestInspectionBasePath = RouteUnder(basePath, ".blueprint/manifests/operations");

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

    /// <summary>
    /// Gives the route pattern of <paramref name="relativeUrl"/> under a base path normalised to end with one slash: the
    /// base path followed by the URL without its leading slash, so a URL declared with or without one routes the same.
    /// </summary>
    private static string RouteUnder(string basePath, string relativeUrl)
    {
        return basePath + relativeUrl.TrimStart('/');
    }

    /// <summary>
    /// Creates an RFC 9727 api-catalog endpoint delegate that returns a Linkset JSON document
    /// pointing to the OpenAPI specification. The response uses <c>application/linkset+json</c>
    /// content type with the RFC 9727 profile. The JSON body and Link header are pre-rendered
    /// per base URI and cached to avoid per-request string interpolation.
    /// </summary>
    private static RequestDelegate CreateApiCatalogDelegate(string openApiPath, string title)
    {
        // Pre-render cache: avoids per-request string interpolation for the common case where scheme/host/pathBase are
        // stable across requests. One immutable entry, read once per request, so a request is always answered with the
        // catalog made from the scheme, host and path base it was compared with, whatever a concurrent request for another
        // host writes, as the router's base-URI cache is.
        ApiCatalogCacheEntry? cache = null;

        return context =>
        {
            var request = context.Request;
            var scheme = request.Scheme;
            var host = request.Host.Value;
            var pathBase = request.PathBase;
            var entry = Volatile.Read(ref cache);

            // The path base is compared by its characters: PathString's own equality ignores case.
            if (entry is null
                || !string.Equals(scheme, entry.Scheme, StringComparison.Ordinal)
                || !string.Equals(host, entry.Host, StringComparison.Ordinal)
                || !string.Equals(pathBase.Value, entry.PathBase.Value, StringComparison.Ordinal))
            {
                var baseUri = $"{scheme}://{host}{pathBase}";
                var openApiHref = baseUri + openApiPath;
                var linkHeader = $"<{openApiHref}>; rel=\"service-desc\"; type=\"application/openapi+json;version=3.0\"";
                var jsonBytes = System.Text.Encoding.UTF8.GetBytes($$"""
                    {
                      "linkset": [
                        {
                          "anchor": "{{baseUri}}/",
                          "service-desc": [
                            {
                              "href": "{{openApiHref}}",
                              "type": "application/openapi+json;version=3.0"
                            }
                          ],
                          "service-doc": [
                            {
                              "href": "{{openApiHref}}",
                              "type": "application/openapi+json;version=3.0",
                              "title": "{{title}}"
                            }
                          ]
                        }
                      ]
                    }
                    """);

                entry = new ApiCatalogCacheEntry(scheme, host, pathBase, linkHeader, jsonBytes);
                Volatile.Write(ref cache, entry);
            }

            context.Response.ContentType = "application/linkset+json; profile=\"https://www.rfc-editor.org/info/rfc9727\"";
            context.Response.Headers["Link"] = entry.LinkHeader;

            if (HttpMethods.IsHead(request.Method))
            {
                return Task.CompletedTask;
            }

            return context.Response.Body.WriteAsync(entry.JsonBytes).AsTask();
        };
    }

    /// <summary>
    /// The api-catalog response made for one scheme, host and path base.
    /// </summary>
    /// <param name="Scheme">The request scheme.</param>
    /// <param name="Host">The request host.</param>
    /// <param name="PathBase">The request's path base.</param>
    /// <param name="LinkHeader">The <c>Link</c> header made from them.</param>
    /// <param name="JsonBytes">The Linkset document made from them.</param>
    private sealed record ApiCatalogCacheEntry(string Scheme, string? Host, PathString PathBase, string LinkHeader, byte[] JsonBytes);

    private class BlueprintApiRouter<TExec> where TExec : BlueprintAppExecutor
    {
        private readonly IServiceProvider _rootServiceProvider;
        private readonly BlueprintApp _app;
        private readonly ILogger<BlueprintApiRouter<TExec>> _logger;
        private readonly BlueprintHttpOptions _httpOptions;
        private readonly PathString _normalizedBasePath;
        private readonly TExec _executor;
        private readonly HttpOperationResultExecutor? _resultExecutor;

        // Cached base URI to avoid per-request string interpolation when scheme/host/path are stable. One immutable entry,
        // read once per request, so a request is always given the base URI made from the scheme, host and path base it was
        // compared with, whatever a concurrent request for another host writes.
        private volatile BaseUriCacheEntry? _baseUriCache;

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

        /// <summary>
        /// Creates a per-operation request delegate that captures the descriptor, eliminating
        /// per-request endpoint metadata lookup on the hot path. The pipeline reference is
        /// resolved lazily on first request to support test scenarios where pipelines are
        /// registered after endpoint mapping.
        /// </summary>
        internal RequestDelegate CreateRequestDelegate(ApiOperationDescriptor operation)
        {
            IOperationExecutorPipeline? cachedPipeline = null;
            return httpContext =>
            {
                cachedPipeline ??= this._executor.GetPipeline(operation.OperationType);
                return this.RouteOperationAsync(httpContext, operation, cachedPipeline);
            };
        }

        /// <summary>
        /// Creates the delegate that answers <c>GET</c> at the app's base path with its root-metadata document, its hrefs
        /// made under the same base URI as every link of an operation request.
        /// </summary>
        internal RequestDelegate CreateRootMetadataDocumentDelegate(
            ImmutableArray<(ApiOperationDescriptor Operation, ImmutableArray<OperationRootLink> Links)> rootLinks)
        {
            return httpContext => HttpOperationResultExecutor.WriteOkJsonAsync(
                httpContext,
                this._app,
                RootMetadataDocument.Create(this.GetBaseUri(httpContext.Request), rootLinks));
        }

        public async Task RouteAsync(HttpContext httpContext)
        {
            var endpoint = httpContext.GetEndpoint()!;
            var operation = endpoint.Metadata.GetMetadata<ApiOperationDescriptor>()!;
            var pipeline = this._executor.GetPipeline(operation.OperationType);

            await this.RouteOperationAsync(httpContext, operation, pipeline).ConfigureAwait(false);
        }

        /// <summary>
        /// Routes an HTTP request through the Blueprint pipeline, using a sync fast-path when
        /// both pipeline execution and response writing complete synchronously (avoiding the
        /// ~176-byte async state machine allocation on simple queries with cached/in-memory handlers).
        /// </summary>
        private Task RouteOperationAsync(HttpContext httpContext, ApiOperationDescriptor operation, IOperationExecutorPipeline pipeline)
        {
            var httpRequest = httpContext.Request;

            // Set the activity display name to the operation type for better trace visibility
            var activity = Activity.Current;
            activity?.DisplayName = operation.Name;

            // Use the ASP.NET Core request scope directly — creating a nested scope wastes ~80 bytes
            // per request and provides no additional isolation since scoped services are already
            // per-request via the framework's scope management.
            var apiContext = new ApiOperationContext(
                httpContext.RequestServices,
                this._app,
                operation,
                httpContext.RequestAborted)
            {
                HttpContext = httpContext,
                UserPrincipal = httpContext.User,

                // Computed per request, stable across requests from the same scheme, host and path base (GetBaseUri).
                BaseUri = this.GetBaseUri(httpRequest),
            };

            // Pipeline.ExecuteAsync never throws in generated code — it wraps all exceptions in
            // OperationResult internally. However, guard defensively in case a custom or hand-rolled
            // pipeline violates this contract (e.g., constructor validation throws synchronously).
            ValueTask<OperationResult> executionTask;
            try
            {
                executionTask = pipeline.ExecuteAsync(apiContext);
            }
            catch (Exception ex)
            {
                return this.HandleUnexpectedExceptionAsync(httpContext, operation, ex);
            }

            if (executionTask.IsCompletedSuccessfully)
            {
                return this.HandleResultSyncPath(httpContext, apiContext, executionTask.Result, operation);
            }

            return this.RouteOperationSlowPathAsync(httpContext, apiContext, executionTask, operation);
        }

        /// <summary>
        /// Gives the request's base URI, for link generation and redirect targets: the scheme, the public host when
        /// configured, the request's path base and the app's base path.
        /// </summary>
        /// <remarks>
        /// Cached to avoid per-request string allocation, checking scheme and host first (cheapest comparisons) to skip
        /// the <c>PathBase.Add()</c> allocation on the hot path. The cache is one immutable entry, read once, so the URI
        /// returned is always the one made from the scheme, host and path base it was compared with.
        /// </remarks>
        private string GetBaseUri(HttpRequest httpRequest)
        {
            var scheme = httpRequest.Scheme;
            var host = this._httpOptions.PublicHost ?? httpRequest.Host.Value;
            var baseUriCache = this._baseUriCache;

            // The path base is compared by its characters: PathString's own equality ignores case, and legacy wrote each
            // request's own path base into its base URI (Signature365 8b40ecc961, ApplicationBuilderExtensions.cs:232).
            if (baseUriCache is null
                || !string.Equals(scheme, baseUriCache.Scheme, StringComparison.Ordinal)
                || !string.Equals(host, baseUriCache.Host, StringComparison.Ordinal)
                || !string.Equals(httpRequest.PathBase.Value, baseUriCache.RequestPathBase.Value, StringComparison.Ordinal))
            {
                var basePath = httpRequest.PathBase.Add(this._normalizedBasePath);
                baseUriCache = new BaseUriCacheEntry(scheme, host, httpRequest.PathBase, $"{scheme}://{host}{basePath}");
                this._baseUriCache = baseUriCache;
            }

            // Hot path: nothing changed, the cached URI is reused with zero allocation.
            return baseUriCache.BaseUri;
        }

        /// <summary>
        /// Handles the result when pipeline execution completed synchronously. If response
        /// writing also completes synchronously, returns <see cref="Task.CompletedTask"/> to
        /// avoid allocating an async state machine Task entirely.
        /// </summary>
        private Task HandleResultSyncPath(
            HttpContext httpContext,
            ApiOperationContext apiContext,
            OperationResult result,
            ApiOperationDescriptor operation)
        {
            var operationResultExecutor = this._resultExecutor ?? DefaultOperationResultExecutor;

            Task<bool> writeTask;
            try
            {
                writeTask = operationResultExecutor.TryExecuteAsync(httpContext, apiContext, result, this._httpOptions);
            }
            catch (Exception ex)
            {
                return this.HandleUnexpectedExceptionAsync(httpContext, operation, ex);
            }

            if (writeTask.IsCompletedSuccessfully)
            {
                if (writeTask.Result == false)
                {
                    // Legacy self-executing results
                    var legacyTask = result.ExecuteAsync(apiContext);
                    return legacyTask.IsCompleted ? Task.CompletedTask : legacyTask;
                }

                return Task.CompletedTask;
            }

            return this.AwaitWriteTaskAsync(httpContext, apiContext, writeTask, result, operation);
        }

        /// <summary>
        /// Awaits a result write that did not complete synchronously, matching the exception handling
        /// <see cref="RouteOperationSlowPathAsync"/> gives a fault reached only after an <c>await</c>: legacy wrote the
        /// exception a failed write threw (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/OkResultOperationExecutor.cs:48-65</c>).
        /// Before this, a fault here had no <c>catch</c> on this path and escaped the request delegate unhandled, which
        /// ASP.NET Core's own top-level handling turns into a bare 500 with no body.
        /// </summary>
        /// <remarks>
        /// The catch is filtered to a response that has not started, matching legacy's own filter exactly
        /// (<c>OkResultOperationExecutor.cs:48</c>, <c>when (httpContext.Response.HasStarted == false)</c>). A fault
        /// reached after the response has started is not writable into a fresh problem body — some of the old response
        /// is already on the wire — so it is left to propagate, as legacy left it (its router had no catch at all), and
        /// ASP.NET Core's top-level handling aborts the connection instead of completing it as if nothing had failed.
        /// </remarks>
        private async Task AwaitWriteTaskAsync(
            HttpContext httpContext,
            ApiOperationContext apiContext,
            Task<bool> writeTask,
            OperationResult result,
            ApiOperationDescriptor operation)
        {
            try
            {
                if (await writeTask.ConfigureAwait(false) == false)
                {
                    await result.ExecuteAsync(apiContext).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!httpContext.Response.HasStarted)
            {
                await this.HandleUnexpectedExceptionAsync(httpContext, operation, ex).ConfigureAwait(false);
            }
        }

        private async Task RouteOperationSlowPathAsync(
            HttpContext httpContext,
            ApiOperationContext apiContext,
            ValueTask<OperationResult> executionTask,
            ApiOperationDescriptor operation)
        {
            try
            {
                var result = await executionTask.ConfigureAwait(false);

                var operationResultExecutor = this._resultExecutor ?? DefaultOperationResultExecutor;
                if (await operationResultExecutor.TryExecuteAsync(httpContext, apiContext, result, this._httpOptions).ConfigureAwait(false) == false)
                {
                    await result.ExecuteAsync(apiContext).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (!httpContext.Response.HasStarted)
            {
                // Same filter as AwaitWriteTaskAsync above, for the same reason: legacy caught a write fault only when
                // the response had not started (OkResultOperationExecutor.cs:48), and had no router catch at all, so a
                // fault reached here after the response started propagates instead of being swallowed into a response
                // that looks complete.
                await this.HandleUnexpectedExceptionAsync(httpContext, operation, ex).ConfigureAwait(false);
            }
        }

        private async Task HandleUnexpectedExceptionAsync(HttpContext httpContext, ApiOperationDescriptor operation, Exception ex)
        {
            var activity = Activity.Current;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            this._logger.LogError(ex, "Unhandled exception escaped pipeline for operation {OperationName}", operation.Name);

            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = 500;
                httpContext.Response.ContentType = this._httpOptions.NegotiateErrorContentType
                    ? LegacyErrorContentTypeNegotiation.Negotiate(httpContext.Request.Headers.Accept)
                    : "application/problem+json";

                // As legacy's writer wrote an exception its result writing threw (Signature365 8b40ecc961,
                // Blueprint.Http/OkResultOperationExecutor.cs:48-65, UnhandledExceptionOperationResultExecutor.cs:30-68).
                var problem = HttpOperationResultExecutor.CreateLegacyExceptionProblem(ex, this._httpOptions.ExposeExceptionDetailsInErrorResponses, activity);

                await HttpOperationResultExecutor.WriteJsonBodyAsync(httpContext, this._app, problem).ConfigureAwait(false);
            }
        }

        /// <summary>A request's scheme, host and path base, and the base URI made from them.</summary>
        /// <param name="Scheme">The request's scheme.</param>
        /// <param name="Host">The host, the public one when configured.</param>
        /// <param name="RequestPathBase">The request's path base.</param>
        /// <param name="BaseUri">The base URI made from them.</param>
        private sealed record BaseUriCacheEntry(string Scheme, string? Host, PathString RequestPathBase, string BaseUri);
    }

    /// <summary>
    /// Compares (string Route, Type OperationType) tuples using ordinal route comparison
    /// for endpoint deduplication.
    /// </summary>
    private sealed class StringTupleComparer : IEqualityComparer<(string Route, Type OperationType)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string Route, Type OperationType) x, (string Route, Type OperationType) y)
        {
            return string.Equals(x.Route, y.Route, StringComparison.Ordinal)
                   && x.OperationType == y.OperationType;
        }

        public int GetHashCode((string Route, Type OperationType) obj)
        {
            return HashCode.Combine(
                obj.Route.GetHashCode(StringComparison.Ordinal),
                obj.OperationType);
        }
    }

    /// <summary>
    /// Compares (string Route, string Method) tuples for cross-operation route collision detection.
    /// </summary>
    private sealed class RouteMethodComparer : IEqualityComparer<(string Route, string Method)>
    {
        public static readonly RouteMethodComparer Instance = new();

        public bool Equals((string Route, string Method) x, (string Route, string Method) y)
        {
            return string.Equals(x.Route, y.Route, StringComparison.Ordinal)
                   && string.Equals(x.Method, y.Method, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Route, string Method) obj)
        {
            return HashCode.Combine(
                obj.Route.GetHashCode(StringComparison.Ordinal),
                obj.Method.GetHashCode(StringComparison.OrdinalIgnoreCase));
        }
    }
}
