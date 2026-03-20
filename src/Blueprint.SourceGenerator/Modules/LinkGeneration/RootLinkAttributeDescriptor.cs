using Blueprint.SourceGenerator.Helpers.Attributes;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contains the descriptor for a RootLinkAttribute.
/// </summary>
public static class RootLinkAttributeDescriptor
{
    /// <summary>
    /// The "url" argument for the RootLinkAttribute.
    /// </summary>
    public static readonly MarkerAttributeConstructorArgument<string, string> UrlArgument = new(
        "url",
        "The route for this link, may contain placeholders (e.g. /users/{Id}).");

    /// <summary>
    /// The RootLinkAttribute descriptor.
    /// </summary>
    public static readonly MarkerAttribute Instance = new("Blueprint.Model.Attributes.RootLinkAttribute")
    {
        Documentation = "The root link for this operation. This registers a URL only, with no links to API resources. Equivalent of Route in ASP.NET.",

        ConstructorArguments =
        {
            UrlArgument,
        },
    };
}
