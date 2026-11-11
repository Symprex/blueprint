namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// The typed client of the upstream template service. The source generator completes it into
/// <c>ITemplateServiceApi</c>, and the gateway's <c>[Expose&lt;TInner&gt;]</c> operations forward through it: each
/// generated forward finds the one client that includes its inner operation, and uses the named HTTP client and JSON
/// options <c>AddBlueprintClient</c> registered for it.
/// </summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(GetTemplateQuery))]
[BlueprintClientInclude(typeof(GetTemplatesQuery))]
[BlueprintClientInclude(typeof(PreviewTemplateCommand))]
public partial class TemplateServiceApi;
