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
/// sample app's generated response-body coverage inventory.
/// </summary>
[JsonSerializable(typeof(TemplateApiResource))]
[JsonSerializable(typeof(TemplateApiResource[]))]
[JsonSerializable(typeof(TemplateListApiResource))]
[JsonSerializable(typeof(TemplateVersionApiResource))]
[JsonSerializable(typeof(TemplateVersionApiResource[]))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
public partial class SampleBlueprintAppJsonContext : JsonSerializerContext;
