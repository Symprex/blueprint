using Blueprint.Http;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles <see cref="DiscardTemplateCommand"/> by removing the template and answering with a deletion made from its self
/// query, which a deletion never runs, so the event carries an href and no data.
/// </summary>
public sealed class DiscardTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Removes the template and signals its deletion.
    /// </summary>
    /// <param name="command">The command naming the template.</param>
    /// <returns>The deletion, made from the template's self query.</returns>
    public ResourceEvent<TemplateApiResource> Handle(DiscardTemplateCommand command)
    {
        store.Delete(command.Id);

        return new ResourceDeleted<TemplateApiResource>(new GetTemplateQuery { Id = command.Id });
    }
}
