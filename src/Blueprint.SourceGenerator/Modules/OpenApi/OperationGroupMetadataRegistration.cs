namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Records the deterministic operation-group name that later descriptor rendering should project into feature data.
/// </summary>
/// <param name="GroupName">The stable group name for the operation.</param>
/// <param name="GroupDescription">An optional human-readable description for the group, sourced from the attribute's Description property.</param>
public sealed record OperationGroupMetadataRegistration(string GroupName, string? GroupDescription = null);
