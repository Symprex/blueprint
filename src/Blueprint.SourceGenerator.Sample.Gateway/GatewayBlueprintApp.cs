using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Blueprint.SourceGenerator.Sample.Gateway;

/// <summary>
/// Gateway Blueprint app that scans the gateway contract assembly for operations.
/// Demonstrates how a gateway service registers its own operations while delegating
/// execution to upstream services via <c>BlueprintHttpClient</c>.
/// </summary>
[BlueprintAppScan(FromAssemblyOf = typeof(ListGatewayTemplatesQuery))]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(GatewayBlueprintAppJsonContext))]
public partial class GatewayBlueprintApp : BlueprintApp;

/// <summary>
/// STJ source-generated serializer context covering all gateway response types.
/// </summary>
[JsonSerializable(typeof(GatewayTemplateResource))]
[JsonSerializable(typeof(GatewayTemplateResource[]))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
public partial class GatewayBlueprintAppJsonContext : JsonSerializerContext;
