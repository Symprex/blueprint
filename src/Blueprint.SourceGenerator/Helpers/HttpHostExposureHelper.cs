using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Centralised detection of whether an operation type has HTTP host exposure via link attributes.
/// An operation is HTTP-exposed if it declares any of the recognised link attributes
/// (<c>RootLinkAttribute</c>, <c>SelfLinkAttribute</c>, or <c>LinkAttribute</c>) from the
/// <c>Blueprint.Model.Attributes</c> namespace.
/// </summary>
internal static class HttpHostExposureHelper
{
    private const string HttpMethodAttributeMetadataName = "Blueprint.Model.Attributes.HttpMethodAttribute";

    /// <summary>
    /// Returns <c>true</c> when the operation symbol carries at least one link attribute
    /// that implies HTTP endpoint registration.
    /// </summary>
    /// <param name="operationType">The operation type symbol to inspect for link attributes.</param>
    /// <returns><c>true</c> if at least one HTTP-exposing link attribute is present; otherwise <c>false</c>.</returns>
    public static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        foreach (var attribute in operationType.GetAttributes())
        {
            if (IsLinkAttribute(attribute.AttributeClass))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Collects all route templates declared on the operation via any link attribute, including
    /// <c>RootLinkAttribute</c>, <c>SelfLinkAttribute</c>, and <c>LinkAttribute</c>.
    /// </summary>
    /// <param name="operationType">The operation type symbol to inspect for link attributes.</param>
    /// <returns>A list of raw route pattern strings (e.g. <c>/users/{Id}</c>). May contain duplicates.</returns>
    /// <remarks>
    /// Each entry is the raw route pattern string (e.g. <c>/users/{Id}</c>). Callers should
    /// deduplicate when needed.
    /// </remarks>
    public static List<string> GetAllRouteTemplates(INamedTypeSymbol operationType)
    {
        var templates = new List<string>();

        foreach (var attribute in operationType.GetAttributes())
        {
            if (TryExtractRouteTemplate(attribute, out var url))
            {
                templates.Add(url);
            }
        }

        return templates;
    }

    /// <summary>
    /// Collects all route templates with their source locations for richer diagnostic reporting.
    /// Each entry pairs a raw route pattern string with the location of the attribute that declared it.
    /// </summary>
    /// <param name="operationType">The operation type symbol to inspect for link attributes.</param>
    /// <returns>Route template strings paired with their source location (if available).</returns>
    public static List<(string Template, LocationInfo? Location)> GetAllRouteTemplatesWithLocations(INamedTypeSymbol operationType)
    {
        var templates = new List<(string, LocationInfo?)>();

        foreach (var attribute in operationType.GetAttributes())
        {
            if (TryExtractRouteTemplate(attribute, out var url))
            {
                var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
                templates.Add((url, location is null ? null : LocationInfo.CreateFrom(location)));
            }
        }

        return templates;
    }

    /// <summary>
    /// Resolves the HTTP methods declared on an operation via explicit HTTP method attributes,
    /// falling back to POST for commands and GET for queries. This is the shared resolution logic
    /// used by both per-operation modules and cross-operation app-level diagnostics.
    /// </summary>
    /// <param name="operationType">The operation type symbol to inspect.</param>
    /// <returns>An array of normalised HTTP method strings (e.g. <c>["GET"]</c>, <c>["POST"]</c>).</returns>
    public static string[] ResolveHttpMethods(INamedTypeSymbol operationType)
    {
        var explicitMethods = new List<string>();

        foreach (var attribute in operationType.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attrClass || !IsOrInheritsFromHttpMethodAttribute(attrClass))
            {
                continue;
            }

            var method = ResolveHttpMethodFromAttribute(attrClass, attribute);
            if (method is not null)
            {
                explicitMethods.Add(method);
            }
        }

        if (explicitMethods.Count > 0)
        {
            return explicitMethods.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return IsCommand(operationType) ? ["POST"] : ["GET"];
    }

    private static string? ResolveHttpMethodFromAttribute(INamedTypeSymbol attrClass, AttributeData attribute)
    {
        var name = attrClass.Name;

        return name switch
        {
            "HttpGetAttribute" => "GET",
            "HttpPostAttribute" => "POST",
            "HttpPutAttribute" => "PUT",
            "HttpPatchAttribute" => "PATCH",
            "HttpDeleteAttribute" => "DELETE",
            _ => attribute.ConstructorArguments.Length > 0
                ? (attribute.ConstructorArguments[0].Value as string)?.Trim().ToUpperInvariant()
                : null,
        };
    }

    private static bool IsOrInheritsFromHttpMethodAttribute(INamedTypeSymbol attributeClass)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToFullMetadataName(), HttpMethodAttributeMetadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.IsInNamespace("Blueprint", "Model")
            && candidate.Name == "ICommand");
    }

    private static bool TryExtractRouteTemplate(AttributeData attribute, out string url)
    {
        url = string.Empty;
        var attrClass = attribute.AttributeClass;

        if (attrClass is null || !IsInAttributeNamespace(attrClass))
        {
            return false;
        }

        var name = attrClass.Name;

        switch (name)
        {
            case "RootLinkAttribute" when attribute.ConstructorArguments.Length >= 1:
                if (attribute.ConstructorArguments[0].Value is string rootUrl && !string.IsNullOrWhiteSpace(rootUrl))
                {
                    url = rootUrl;
                    return true;
                }

                break;

            case "SelfLinkAttribute" when attribute.ConstructorArguments.Length >= 2:
                if (attribute.ConstructorArguments[1].Value is string selfUrl && !string.IsNullOrWhiteSpace(selfUrl))
                {
                    url = selfUrl;
                    return true;
                }

                break;

            case "LinkAttribute" when attribute.ConstructorArguments.Length >= 2:
                if (attribute.ConstructorArguments[1].Value is string linkUrl && !string.IsNullOrWhiteSpace(linkUrl))
                {
                    url = linkUrl;
                    return true;
                }

                break;
        }

        return false;
    }

    private static bool IsLinkAttribute(INamedTypeSymbol? attrClass)
    {
        if (attrClass is null)
        {
            return false;
        }

        if (!IsInAttributeNamespace(attrClass))
        {
            return false;
        }

        return attrClass.Name is "RootLinkAttribute" or "SelfLinkAttribute" or "LinkAttribute";
    }

    private static bool IsInAttributeNamespace(INamedTypeSymbol attrClass)
    {
        return attrClass.IsInNamespace("Blueprint", "Model", "Attributes");
    }
}
