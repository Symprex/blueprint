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
    /// <returns>A builder that allows adding metadata / conventions to all mapped endpoints.</returns>
    public static IEndpointConventionBuilder MapBlueprintApi<T, TExec>(
        this IEndpointRouteBuilder endpoints,
        string basePath,
        Action<OpenApiInfoOptions>? configureOpenApi = null) where T : BlueprintApp, new()
                         where TExec : BlueprintAppExecutor
    {
        // Ensure ends with a slash, but only one
        basePath = basePath.TrimEnd('/') + '/';

        // var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Blueprint.Compilation");
        var runtimeModel = new T();

        var httpOptions = endpoints.ServiceProvider.GetService<IOptions<BlueprintHttpOptions>>()?.Value ?? new BlueprintHttpOptions();

        // var apiOperationExecutor = endpoints.ServiceProvider.GetRequiredService<IApiOperationExecutor>();

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

        var openApiBuilder = endpoints.Map(
            RoutePatternFactory.Parse(basePath.TrimEnd('/') + "/.blueprint/openapi.json"),
            OpenApiDocumentEndpoint.Create(runtimeModel, infoOptions));
        openApiBuilder.WithDisplayName("GET Blueprint OpenAPI document");
        openApiBuilder.WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

        builders.Add(openApiBuilder);

        // Ordering by 'indexOf {' means we put those URLs which are not placeholders
        // first (e.g. /users/{id} and /users/me will put /users/me first) because those without would return -1
        // foreach (var link in apiDataModel.Links.OrderBy(l => l.UrlFormat.IndexOf('{')))
        // {
        //     var httpFeatureData = link.OperationDescriptor.GetFeatureData<HttpOperationFeatureData>();
        //
        //     var builder = endpoints.Map(RoutePatternFactory.Parse(basePath + link.RoutingUrl), requestDelegate);
        //
        //     builder.WithDisplayName($"{httpFeatureData.HttpMethod} {link.OperationDescriptor.Name}");
        //     builder.WithMetadata(new HttpMethodMetadata(new[] { httpFeatureData.HttpMethod }));
        //     builder.WithMetadata(link.OperationDescriptor);
        //
        //     builders.Add(builder);
        // }

        foreach (var operation in runtimeModel.Operations)
        {
            foreach (var url in operation.Urls)
            {
                var builder = endpoints.Map(RoutePatternFactory.Parse(basePath.TrimEnd('/') + url.UrlFormat), requestDelegate);
                builder.WithMetadata(operation);
                builder.WithDisplayName($"GET {operation.Name}");

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
        private readonly string _basePath;

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
            this._basePath = basePath;
        }

        public async Task RouteAsync(HttpContext httpContext)
        {
            var endpoint = httpContext.GetEndpoint()!;
            var routeData = httpContext.GetRouteData();
            var httpRequest = httpContext.Request;

            var operation = endpoint.Metadata.GetMetadata<ApiOperationDescriptor>()!;

            // If we have an activity set the DisplayName to the operation type
            if (Activity.Current != null)
            {
                Activity.Current.DisplayName = operation.Name;
            }

            // var httpFeatureData = operation.GetFeatureData<HttpOperationFeatureData>();
            //
            // if (httpFeatureData.HttpMethod != httpRequest.Method)
            // {
            //     this._logger.LogInformation(
            //         "Request {Method} {Url} does not match required HTTP method {RequiredMethod}",
            //         httpRequest.Method,
            //         httpRequest.GetDisplayUrl(),
            //         httpFeatureData.HttpMethod);
            //
            //     httpContext.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            //
            //     return;
            // }

            using var nestedContainer = this._rootServiceProvider.CreateScope();

            var apiContext = new ApiOperationContext(
                nestedContainer.ServiceProvider,
                this._app,
                operation,
                httpContext.RequestAborted)
            {
                // ClaimsIdentity = httpContext.User.Identity as ClaimsIdentity,
            };

            // apiContext.SetHttpFeatureContext(new HttpFeatureContext
            // {
            //     HttpContext = httpContext,
            //     RouteData = routeData,
            // });

            // var request = httpContext.Request;
            // var baseUri = $"{request.Scheme}://{this._httpOptions.Value.PublicHost ?? request.Host.Value}{request.PathBase}/{this._basePath}";
            //
            // httpContext.SetBaseUri(baseUri);

            var result = await nestedContainer.ServiceProvider.GetRequiredService<TExec>().GetPipeline(operation.OperationType).ExecuteAsync(apiContext);

            var operationResultExecutor = nestedContainer.ServiceProvider.GetService<HttpOperationResultExecutor>() ?? DefaultOperationResultExecutor;
            if (await operationResultExecutor.TryExecuteAsync(httpContext, apiContext, result, this._httpOptions) == false)
            {
                // Legacy self-executing results still flow through the old path until the broader host-translation workstream lands.
                await result.ExecuteAsync(apiContext);
            }
        }
    }
}
