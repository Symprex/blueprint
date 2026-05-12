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
    private const string AttributeNamespace = "Blueprint.Model.Attributes";

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
            var attrClass = attribute.AttributeClass;

            if (attrClass is null || !IsInAttributeNamespace(attrClass))
            {
                continue;
            }

            var name = attrClass.Name;

            switch (name)
            {
                case "RootLinkAttribute" when attribute.ConstructorArguments.Length >= 1:
                    {
                        // RootLinkAttribute(string url)
                        if (attribute.ConstructorArguments[0].Value is string url && !string.IsNullOrWhiteSpace(url))
                        {
                            templates.Add(url);
                        }

                        break;
                    }

                case "SelfLinkAttribute" when attribute.ConstructorArguments.Length >= 2:
                    {
                        // SelfLinkAttribute(Type resourceType, string url)
                        if (attribute.ConstructorArguments[1].Value is string url && !string.IsNullOrWhiteSpace(url))
                        {
                            templates.Add(url);
                        }

                        break;
                    }

                case "LinkAttribute" when attribute.ConstructorArguments.Length >= 2:
                    {
                        // LinkAttribute(Type? resourceType, string routePattern, string? rel = null)
                        if (attribute.ConstructorArguments[1].Value is string url && !string.IsNullOrWhiteSpace(url))
                        {
                            templates.Add(url);
                        }

                        break;
                    }
            }
        }

        return templates;
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
        return string.Equals(
            attrClass.ContainingNamespace?.ToDisplayString(),
            AttributeNamespace,
            StringComparison.Ordinal);
    }
}
