using System.Text.Json.Serialization;
using Blueprint.Model;
using Microsoft.AspNetCore.Mvc;

namespace Blueprint.Mcp.Tests.App;

/// <summary>
/// The app the tool tests serve over MCP: every operation in this assembly.
/// </summary>
[BlueprintAppScan]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(McpTestAppJsonContext))]
public partial class McpTestBlueprintApp : BlueprintApp;

/// <summary>The serializer context of the test app.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
public partial class McpTestAppJsonContext : JsonSerializerContext;
