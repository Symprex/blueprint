namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Gateway handler that creates a new template in the upstream service via HTTP POST.
/// Demonstrates the HTTP client's ability to serialize a command operation as a JSON body
/// and send it as a POST request to the upstream service.
/// </summary>
public class CreateGatewayTemplateCommandHandler(BlueprintHttpClient<SampleBlueprintApp> upstreamClient)
{
    /// <summary>
    /// Sends a <see cref="CreateTemplateCommand"/> to the upstream service and maps the result
    /// to a <see cref="GatewayTemplateResource"/>.
    /// </summary>
    /// <param name="command">The gateway create command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created template mapped to the gateway resource type.</returns>
    public async Task<GatewayTemplateResource> Handle(
        CreateGatewayTemplateCommand command,
        CancellationToken cancellationToken)
    {
        var upstream = await upstreamClient.SendAsync<TemplateApiResource>(
            new CreateTemplateCommand
            {
                Name = command.Name,
                HtmlSource = command.HtmlSource,
                PlainTextSource = command.PlainTextSource,
            },
            cancellationToken);

        return new GatewayTemplateResource
        {
            Id = upstream!.Id,
            Name = upstream.Name,
            IsArchived = upstream.IsArchived,
        };
    }
}
