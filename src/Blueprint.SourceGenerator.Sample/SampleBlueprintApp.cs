using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Microsoft.AspNetCore.Mvc;

namespace Blueprint.SourceGenerator.Sample;

/// <summary>
/// Sample Blueprint app used to demonstrate scan-based registration against contract-assembly operations.
/// </summary>
[BlueprintAppScan(FromAssemblyOf = typeof(GetTemplatesQuery))]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(SampleBlueprintAppJsonContext))]
public partial class SampleBlueprintApp : BlueprintApp;

/// <summary>
/// Demonstrates the app-owned serializer context declaration seam that later OpenAPI validation will compare against the
/// sample app's generated response-body coverage inventory. Consumers control JSON serialization behavior via
/// <see cref="JsonSourceGenerationOptionsAttribute"/> on this context class — for example setting
/// <c>DefaultIgnoreCondition</c> or <c>PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase</c>.
/// </summary>
/// <remarks>
/// Note: .NET 8's STJ source generator has a known codegen bug (CS0128 duplicate local) when
/// <c>[JsonSourceGenerationOptions]</c> is applied to a context that includes types with <c>object</c>-typed
/// properties (e.g., <see cref="ProblemDetails"/>). This is fixed in .NET 9. Until then, control property naming
/// per-property with <c>[JsonPropertyName]</c> or separate the problem types into a second context.
/// </remarks>
[JsonSerializable(typeof(TemplateApiResource))]
[JsonSerializable(typeof(TemplateApiResource[]))]
[JsonSerializable(typeof(TemplateListApiResource))]
[JsonSerializable(typeof(PagedApiResource<TemplateApiResource>))]
[JsonSerializable(typeof(TemplateVersionApiResource))]
[JsonSerializable(typeof(TemplateVersionApiResource[]))]
[JsonSerializable(typeof(TemplatePreviewApiResource))]
[JsonSerializable(typeof(ExportJobApiResource))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
public partial class SampleBlueprintAppJsonContext : JsonSerializerContext;
