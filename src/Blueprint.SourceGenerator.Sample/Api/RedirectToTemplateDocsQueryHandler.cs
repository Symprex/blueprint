using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="RedirectToTemplateDocsQuery"/> by redirecting to an external documentation URL.
/// Demonstrates how <see cref="RedirectResult"/> sends an HTTP redirect response with a <c>Location</c>
/// header and configurable status code (default 302 Found).
/// </summary>
public class RedirectToTemplateDocsQueryHandler
{
    /// <summary>
    /// Redirects to the external documentation page for the requested template.
    /// </summary>
    /// <param name="query">The redirect query containing the template identifier.</param>
    /// <returns>A <see cref="RedirectResult"/> pointing to the documentation URL.</returns>
    public RedirectResult Handle(RedirectToTemplateDocsQuery query)
    {
        return new RedirectResult($"https://docs.example.com/templates/{query.Id}");
    }
}
