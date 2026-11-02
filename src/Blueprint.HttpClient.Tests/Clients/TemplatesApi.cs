using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.HttpClient.Tests.Clients;

/// <summary>
/// A typed client over the sample contract's operations, included from that referenced assembly, plus one operation
/// declared here, beside the client, whose request builders are generated in this same compilation.
/// </summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(SearchTemplatesQuery))]
[BlueprintClientInclude(typeof(GetTemplateQuery))]
[BlueprintClientInclude(typeof(GetTemplateByStrongIdQuery))]
[BlueprintClientInclude(typeof(GetTemplatesQuery))]
[BlueprintClientInclude(typeof(GetPagedTemplatesQuery))]
[BlueprintClientInclude(typeof(GetTemplateHistoryQuery))]
[BlueprintClientInclude(typeof(GetTemplateNotificationRulesQuery))]
#pragma warning disable CS0618 // An obsolete operation can still be included.
[BlueprintClientInclude(typeof(GetTemplateVersionsQuery))]
#pragma warning restore CS0618
[BlueprintClientInclude(typeof(GetMyAuthManifestQuery))]
[BlueprintClientInclude(typeof(HealthCheckQuery))]
[BlueprintClientInclude(typeof(ArchiveTemplateCommand))]
[BlueprintClientInclude(typeof(BulkArchiveTemplatesCommand))]
[BlueprintClientInclude(typeof(ChangePasswordCommand))]
[BlueprintClientInclude(typeof(CloneTemplateCommand))]
[BlueprintClientInclude(typeof(CreateTemplateCommand))]
[BlueprintClientInclude(typeof(PatchTemplateSettingsCommand))]
[BlueprintClientInclude(typeof(PreviewTemplateCommand))]
[BlueprintClientInclude(typeof(PublishTemplateCommand))]
[BlueprintClientInclude(typeof(SetTemplateNotificationPriorityCommand))]
[BlueprintClientInclude(typeof(UpdateTemplateCommand))]
[BlueprintClientInclude(typeof(RegisterTemplateCommand))]
public partial class TemplatesApi
{
    private readonly required string _region;

    /// <summary>
    /// Gets the region the client was made for, which a readonly field turns into a constructor parameter.
    /// </summary>
    public string Region => this._region;

    /// <summary>
    /// A clone that adds <paramref name="step"/> to the <c>X-Order</c> header of every request, after any steps already added.
    /// </summary>
    /// <param name="step">The step to record.</param>
    /// <returns>The clone.</returns>
    public ITemplatesApi WithStep(string step)
    {
        return this.WithRequestModifier(request => request.Headers.Add("X-Order", step));
    }

    partial void OnConfigure(GetTemplateQuery operation, ref CallOptions? options)
    {
        var configured = operation.Id;
        var inner = options?.ModifyRequest;

        options = new CallOptions
        {
            ModifyRequest = request =>
            {
                inner?.Invoke(request);
                request.Headers.Add("X-Configured", configured);
            },
        };
    }
}

/// <summary>
/// An operation declared beside the client, returning a legacy resource event.
/// </summary>
[RootLink("/templates/register")]
[HttpPost]
public partial class RegisterTemplateCommand : ICommand<ResourceCreated<TemplateApiResource>>
{
    /// <summary>
    /// Gets or sets the template's name.
    /// </summary>
    public string? Name { get; set; }
}
