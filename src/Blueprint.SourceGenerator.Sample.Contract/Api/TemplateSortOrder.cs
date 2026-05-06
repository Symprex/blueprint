namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Defines the available sort orders for template search results.
/// Used to demonstrate OpenAPI <c>enum</c> rendering for enum-typed properties.
/// </summary>
public enum TemplateSortOrder
{
    /// <summary>Sort by template name alphabetically.</summary>
    Name,

    /// <summary>Sort by creation date, newest first.</summary>
    CreatedDate,

    /// <summary>Sort by last modification date, newest first.</summary>
    LastModified,
}
