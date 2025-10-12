namespace Blueprint.Model;

/// <summary>
/// Represents a URL that can be used to access an operation. These are separate from <see cref="ApiOperationLink"/>s
/// because they are not associated with a particular resource, but are used to access the operation itself.
/// </summary>
/// <remarks>
/// Typically an operation will be attributed with link attributes that will both register URLs and links, but in the
/// data model we keep these separate to better identify what is required for each as they can serve slightly different
/// purposes (for example a URL needs only worry about the possible "route values" that map to the operation, but a link
/// needs to care about the resource from which it is linked plus the route values to map to).
/// </remarks>
/// <param name="UrlFormat">The URL format for this URL, using the ASP.NET route syntax.</param>
public record ApiOperationUrl(string UrlFormat);
