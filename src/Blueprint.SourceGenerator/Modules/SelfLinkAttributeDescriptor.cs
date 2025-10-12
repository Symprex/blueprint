using Blueprint.SourceGenerator.Helpers.Attributes;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contains the descriptor for a RootLinkAttribute.
/// </summary>
public static class SelfLinkAttributeDescriptor
{
    /// <summary>
    /// The "url" argument for the RootLinkAttribute.
    /// </summary>
    public static readonly MarkerAttributeConstructorArgument<Type, ITypeSymbol> ResourceTypeArgument = new(
        "resourceType",
        "The type of resource this link applies to");

    /// <summary>
    /// The "url" argument for the RootLinkAttribute.
    /// </summary>
    public static readonly MarkerAttributeConstructorArgument<string, string> UrlArgument = new(
        "url",
        "The route for this link, may contain placeholders (e.g. /users/{Id} or /users/{OperationId:RouteId}).");

    /// <summary>
    /// The RootLinkAttribute descriptor.
    /// </summary>
    public static readonly MarkerAttribute Instance = new("Blueprint.Model.Attributes.SelfLinkAttribute")
    {
        Documentation = "A \"self\" link for this operation, linking it from the resource to itself.",

        ConstructorArguments =
        {
            ResourceTypeArgument,
            UrlArgument,
        },
    };
}
