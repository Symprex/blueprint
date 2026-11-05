using JetBrains.Annotations;

namespace Blueprint.Hangfire;

/// <summary>
/// The background task a Hangfire job this package created carries, as <see cref="HangfireTaskJobDescriber"/> reads it.
/// </summary>
/// <param name="Name">The task type's own name, without its namespace or assembly: <c>ImportEntraIdBackgroundTask</c>.</param>
/// <param name="TaskTypeName">The task type's assembly-qualified name, as the job stores it.</param>
/// <param name="TaskJson">The task's properties, as the JSON the job stores and runs it from.</param>
[PublicAPI]
public sealed record HangfireTaskJobDescription(string Name, string TaskTypeName, string TaskJson);
