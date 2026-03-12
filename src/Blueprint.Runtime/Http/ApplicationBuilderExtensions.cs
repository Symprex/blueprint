using System.Diagnostics;
using Blueprint;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Routing.Patterns;

// This is the recommendation from MS for extensions to IEndpointRouteBuilder to aid discoverability
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps all Blueprint operation endpoints from the given <see cref="BlueprintAppExecutor" />.
    /// </summary>
    /// <param name="endpoints">The endpoint builder to register with.</param>
    /// <param name="basePath">A base path to prepend to all routes.</param>
    /// <returns>A builder that allows adding metadata / conventions to all mapped endpoints.</returns>
    public static IEndpointConventionBuilder MapBlueprintApi<T, TExec>(
        this IEndpointRouteBuilder endpoints,
        string basePath) where T : BlueprintApp, new()
                         where TExec : BlueprintAppExecutor
    {
        // Ensure ends with a slash, but only one
        basePath = basePath.TrimEnd('/') + '/';

        // var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Blueprint.Compilation");
        var runtimeModel = new T();

        // var httpOptions = endpoints.ServiceProvider.GetRequiredService<IOptions<BlueprintHttpOptions>>();
        var httpOptions = new BlueprintHttpOptions();

        // var apiOperationExecutor = endpoints.ServiceProvider.GetRequiredService<IApiOperationExecutor>();

        RequestDelegate requestDelegate = new BlueprintApiRouter<TExec>(
            endpoints.ServiceProvider,
            runtimeModel,
            endpoints.ServiceProvider.GetRequiredService<ILogger<BlueprintApiRouter<TExec>>>(),
            httpOptions,
            basePath).RouteAsync;

        var builders = new List<IEndpointConventionBuilder>();

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

            // We want to immediately execute the result to allow it to write to the HTTP response
            await result.ExecuteAsync(apiContext);
        }
    }
}
