using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="CreateTemplateCommand"/> by returning a deterministic template resource
/// built from the command properties. Uses a simple counter-based ID for in-memory demonstration.
/// </summary>
public class CreateTemplateCommandHandler
{
    private static int _nextId;

    /// <summary>
    /// Creates a new template from the validated command properties.
    /// </summary>
    /// <param name="command">The validated create-template command.</param>
    /// <returns>The created template resource.</returns>
    public TemplateApiResource Handle(CreateTemplateCommand command)
    {
        var id = $"template-{Interlocked.Increment(ref _nextId):D3}";

        return new TemplateApiResource(
            id,
            command.HtmlSource ?? string.Empty,
            command.PlainTextSource ?? string.Empty);
    }
}
