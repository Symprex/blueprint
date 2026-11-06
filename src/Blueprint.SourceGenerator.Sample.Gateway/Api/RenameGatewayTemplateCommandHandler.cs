namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Gateway handler that renames a template by forwarding an <see cref="UpdateTemplateCommand"/> with the operation's
/// context, so the command's <c>[TranslateError]</c> answers an upstream <c>not_found</c> as the gateway's own problem.
/// </summary>
public class RenameGatewayTemplateCommandHandler(BlueprintHttpClient<SampleBlueprintApp> upstreamClient)
{
    /// <summary>
    /// Forwards a fresh <see cref="UpdateTemplateCommand"/> and maps the updated template to the gateway's resource.
    /// </summary>
    /// <param name="command">The gateway rename command.</param>
    /// <param name="context">The gateway operation being served, whose caller and attributes the forward uses.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The renamed template.</returns>
    public async Task<GatewayTemplateResource> Handle(
        RenameGatewayTemplateCommand command,
        ApiOperationContext context,
        CancellationToken cancellationToken)
    {
        var upstream = await upstreamClient.ForwardAsync<UpdateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            context,
            new UpdateTemplateCommand { Id = command.Id, Name = command.Name, HtmlSource = command.HtmlSource },
            cancellationToken: cancellationToken);

        return new GatewayTemplateResource
        {
            Id = upstream.Content.Id,
            Name = upstream.Content.Name,
            IsArchived = upstream.Content.IsArchived,
        };
    }
}
