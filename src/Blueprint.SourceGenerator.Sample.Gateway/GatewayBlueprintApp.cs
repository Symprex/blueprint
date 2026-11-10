using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Blueprint.SourceGenerator.Sample.Gateway;

/// <summary>
/// Gateway Blueprint app. It scans its own assembly, whose reads are <c>[Expose&lt;TInner&gt;]</c> operations that forward to the
/// upstream template service through <see cref="Api.TemplateServiceApi"/>, and the gateway contract assembly, whose create
/// command has a hand-written handler calling the upstream with <c>BlueprintHttpClient</c>.
/// </summary>
[BlueprintAppScan]
[BlueprintAppScan(FromAssemblyOf = typeof(CreateGatewayTemplateCommand))]
[BlueprintHttpClientExtensions]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(GatewayBlueprintAppJsonContext))]
public partial class GatewayBlueprintApp : BlueprintApp;

/// <summary>
/// STJ source-generated serializer context covering all gateway response types.
/// </summary>
[JsonSerializable(typeof(GatewayTemplateResource))]
[JsonSerializable(typeof(GatewayTemplateResource[]))]
[JsonSerializable(typeof(TemplateApiResource))]
[JsonSerializable(typeof(TemplateApiResource[]))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
public partial class GatewayBlueprintAppJsonContext : JsonSerializerContext;
