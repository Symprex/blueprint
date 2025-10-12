using System.Diagnostics.CodeAnalysis;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Operation handlers for template related operations.
/// </summary>
/// <seealso cref="GetTemplatesQuery"/>
public class GetTemplatesQueryHandler
{
    /// <summary>
    /// Handles the <see cref="GetTemplatesQuery" /> operation.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>All available templates.</returns>
    [SuppressMessage("Usage", "CA1801", Justification = "First parameter used to indicate type we are handling")]
    public TemplateApiResource[] HandleAsync(GetTemplatesQuery query)
    {
        throw new NotImplementedException("Cannot get templates");
    }
}
