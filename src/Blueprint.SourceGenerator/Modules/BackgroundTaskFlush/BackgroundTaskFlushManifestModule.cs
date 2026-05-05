using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules.BackgroundTaskFlush;

/// <summary>
/// Contributes <c>backgroundTasks.flushEnabled</c> to the per-operation debug manifest,
/// reflecting whether the post-execution background-task flush stage is active.
/// </summary>
/// <remarks>
/// Unlike <see cref="BackgroundTaskFlushModule"/> which always participates in every pipeline,
/// this manifest entry always reports <c>True</c> because the flush module runs unconditionally
/// for all operation types (guarded at runtime by <c>handlerResult.WasSuccessful</c> and the
/// presence of <c>IBackgroundTaskScheduler</c> in DI).
/// </remarks>
internal sealed class BackgroundTaskFlushManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(BackgroundTaskFlushManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(BackgroundTaskFlushManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        // BackgroundTaskFlushModule always contributes to every pipeline unconditionally.
        // The actual flush is guarded at runtime by WasSuccessful and DI availability.
        context.AddEntry("backgroundTasks.flushEnabled", "True");
    }
}
