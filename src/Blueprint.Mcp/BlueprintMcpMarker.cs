namespace Blueprint.Mcp;

/// <summary>
/// Registered by <c>AddBlueprintMcp</c>, so <c>MapBlueprintMcp</c> can say what is missing when it was not called,
/// rather than fail inside the MCP SDK's own mapping.
/// </summary>
internal sealed class BlueprintMcpMarker;
