using System.Reflection;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.HttpClient;

/// <summary>
/// A <see cref="BlueprintApp"/> variant that populates operation descriptors at runtime by scanning
/// a contract assembly for <see cref="IOperation"/> implementations and their link/HTTP-method attributes.
/// This is used by <see cref="BlueprintHttpClientServiceCollectionExtensions.AddBlueprintClient{TInterface,TImplementation}(Microsoft.Extensions.DependencyInjection.IServiceCollection,Action{IServiceProvider,System.Net.Http.HttpClient},Assembly?)"/>
/// to bootstrap typed HTTP clients without requiring a source-generated <see cref="BlueprintApp"/>.
/// </summary>
internal sealed class RuntimeScanBlueprintApp : BlueprintApp
{
    private RuntimeScanBlueprintApp()
    {
    }

    /// <summary>
    /// Scans the specified assembly for <see cref="IOperation"/> types and builds a frozen
    /// <see cref="BlueprintApp"/> that the <see cref="BlueprintHttpClient{TApp}"/> can use
    /// for descriptor lookups, URL resolution, and HTTP method determination.
    /// </summary>
    /// <param name="assembly">The assembly to scan (typically the contract assembly).</param>
    /// <returns>A fully-populated, frozen <see cref="RuntimeScanBlueprintApp"/>.</returns>
    internal static RuntimeScanBlueprintApp ScanAssembly(Assembly assembly)
    {
        var app = new RuntimeScanBlueprintApp();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!typeof(IOperation).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            var descriptor = CreateDescriptor(type);

            if (descriptor is not null)
            {
                app.Register(descriptor);
            }
        }

        app.Freeze();

        return app;
    }

    private static ApiOperationDescriptor? CreateDescriptor(Type operationType)
    {
        // Derive operation name by stripping Query/Command suffix
        var name = operationType.Name;
        if (name.EndsWith("Query", StringComparison.Ordinal))
        {
            name = name[..^5];
        }
        else if (name.EndsWith("Command", StringComparison.Ordinal))
        {
            name = name[..^7];
        }

        var descriptor = new ApiOperationDescriptor(operationType, name, "RuntimeScan");

        // Populate links from [Link], [SelfLink], [RootLink] attributes
        var linkAttributes = operationType.GetCustomAttributes<LinkAttribute>(false);
        var hasLinks = false;

        foreach (var linkAttr in linkAttributes)
        {
            hasLinks = true;
            var routePattern = linkAttr.RoutePattern;
            var resourceType = linkAttr.ResourceType ?? typeof(object);
            var rel = linkAttr.Rel ?? (linkAttr is SelfLinkAttribute ? "self" : name);

            // RoutingUrl strips type specifiers from placeholders: {Id:string} → {Id}
            var routingUrl = StripPlaceholderTypes(routePattern);

            var link = new ApiOperationLink(descriptor, routingUrl, routePattern, rel, resourceType);
            descriptor.AddLink(link);
            descriptor.AddUrl(new ApiOperationUrl(routePattern));
        }

        if (!hasLinks)
        {
            // Operations without links can't be called via HTTP — skip them
            return null;
        }

        // Populate HTTP method from [HttpGet], [HttpPost], etc. attributes
        var httpMethodAttrs = operationType.GetCustomAttributes<HttpMethodAttribute>(false).ToList();

        if (httpMethodAttrs.Count > 0)
        {
            var methods = httpMethodAttrs.Select(static a => a.HttpMethod).Distinct(StringComparer.OrdinalIgnoreCase);
            descriptor.SetFeatureData(new OperationHttpMethodMetadataFeatureData([.. methods]));
        }
        else
        {
            // Convention: IQuery → GET, ICommand → POST
            var isQuery = operationType.GetInterfaces()
                .Any(static i => i.IsGenericType && i.GetGenericTypeDefinition().Name.StartsWith("IQuery", StringComparison.Ordinal));

            descriptor.SetFeatureData(isQuery
                ? WellKnownFeatureData.HttpGet
                : WellKnownFeatureData.HttpPost);
        }

        return descriptor;
    }

    /// <summary>
    /// Strips type specifiers from URL placeholders: <c>{Id:string}</c> → <c>{Id}</c>.
    /// </summary>
    private static string StripPlaceholderTypes(string url)
    {
        // Simple approach: find {X:Y} patterns and replace with {X}
        var result = url;
        var startIdx = 0;

        while (startIdx < result.Length)
        {
            var open = result.IndexOf('{', startIdx);
            if (open < 0)
            {
                break;
            }

            var close = result.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }

            var content = result[(open + 1)..close];
            var colonIdx = content.IndexOf(':');

            if (colonIdx >= 0)
            {
                var paramName = content[..colonIdx];
                result = string.Concat(result.AsSpan(0, open + 1), paramName, result.AsSpan(close));
                startIdx = open + paramName.Length + 2; // past the closing }
            }
            else
            {
                startIdx = close + 1;
            }
        }

        return result;
    }
}
