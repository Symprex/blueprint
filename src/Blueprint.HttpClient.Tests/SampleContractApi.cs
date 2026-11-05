using Blueprint.SourceGenerator.Sample.Contract.Api;

#pragma warning disable CS0618 // GetTemplateVersionsQuery is obsolete; a client of the contract still calls it.

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// A typed client over every operation of the sample contract a typed client can call. The operations live in a referenced
/// assembly built with Blueprint's generator, so the client calls the request builders that assembly carries, as a client
/// in a consumer's contract assembly calls those of a contract it references.
/// </summary>
/// <remarks>
/// <see cref="ValidateTemplateCommand"/> is not included: it declares no result, so a typed client has no method to give it.
/// <see cref="SendTemplateSharedNotificationTask"/> is a background task with no URL.
/// </remarks>
[BlueprintClient]
[BlueprintClientInclude(typeof(ArchiveTemplateCommand))]
[BlueprintClientInclude(typeof(BulkArchiveTemplatesCommand))]
[BlueprintClientInclude(typeof(ChangePasswordCommand))]
[BlueprintClientInclude(typeof(CloneTemplateCommand))]
[BlueprintClientInclude(typeof(CreateTemplateCommand))]
[BlueprintClientInclude(typeof(ExportTemplatesCommand))]
[BlueprintClientInclude(typeof(PatchTemplateSettingsCommand))]
[BlueprintClientInclude(typeof(PreviewTemplateCommand))]
[BlueprintClientInclude(typeof(PublishTemplateCommand))]
[BlueprintClientInclude(typeof(SetTemplateNotificationPriorityCommand))]
[BlueprintClientInclude(typeof(UpdateTemplateCommand))]
[BlueprintClientInclude(typeof(UpdateTemplateSettingsCommand))]
[BlueprintClientInclude(typeof(DownloadTemplateQuery))]
[BlueprintClientInclude(typeof(GetFolderContentsQuery))]
[BlueprintClientInclude(typeof(GetMyAuthManifestQuery))]
[BlueprintClientInclude(typeof(GetPagedTemplatesQuery))]
[BlueprintClientInclude(typeof(GetTemplateByStrongIdQuery))]
[BlueprintClientInclude(typeof(GetTemplateHistoryQuery))]
[BlueprintClientInclude(typeof(GetTemplateNotificationRulesQuery))]
[BlueprintClientInclude(typeof(GetTemplateQuery))]
[BlueprintClientInclude(typeof(GetTemplateVersionsQuery))]
[BlueprintClientInclude(typeof(GetTemplatesQuery))]
[BlueprintClientInclude(typeof(HealthCheckQuery))]
[BlueprintClientInclude(typeof(RedirectToTemplateDocsQuery))]
[BlueprintClientInclude(typeof(SearchTemplatesQuery))]
public partial class SampleContractApi;
