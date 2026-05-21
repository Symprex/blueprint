using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator;

/// <summary>
/// A configuration for the repository's source generators.
/// </summary>
/// <param name="Modules">The modules to use to configure the source generator.</param>
public record BlueprintSourceGenerationConfig(IReadOnlyList<IBlueprintSourceGeneratorModule> Modules)
{
    /// <summary>
    /// Builds the ordered contributor execution plan for the configured modules.
    /// </summary>
    /// <returns>The execution plan, or diagnostics if the composition is invalid.</returns>
    public Result<BlueprintModuleExecutionPlan> BuildExecutionPlan()
    {
        var postInitialization = new List<ContributorRegistration<IPostInitializationContributor>>();
        var descriptor = new List<ContributorRegistration<IDescriptorContributor>>();
        var exceptionTranslation = new List<ContributorRegistration<IExceptionTranslationContributor>>();
        var pipeline = new List<ContributorRegistration<IPipelineContributor>>();
        var host = new List<ContributorRegistration<IHostContributor>>();
        var appRegistration = new List<ContributorRegistration<IAppRegistrationContributor>>();
        var debugManifest = new List<ContributorRegistration<IDebugManifestContributor>>();

        foreach (var module in this.Modules)
        {
            if (module is IPostInitializationContributor postInitializationContributor)
            {
                postInitialization.Add(new ContributorRegistration<IPostInitializationContributor>(module, postInitializationContributor));
            }

            if (module is IDescriptorContributor descriptorContributor)
            {
                descriptor.Add(new ContributorRegistration<IDescriptorContributor>(module, descriptorContributor));
            }

            if (module is IExceptionTranslationContributor exceptionTranslationContributor)
            {
                exceptionTranslation.Add(new ContributorRegistration<IExceptionTranslationContributor>(module, exceptionTranslationContributor));
            }

            if (module is IPipelineContributor pipelineContributor)
            {
                pipeline.Add(new ContributorRegistration<IPipelineContributor>(module, pipelineContributor));
            }

            if (module is IHostContributor hostContributor)
            {
                host.Add(new ContributorRegistration<IHostContributor>(module, hostContributor));
            }

            if (module is IAppRegistrationContributor appRegistrationContributor)
            {
                appRegistration.Add(new ContributorRegistration<IAppRegistrationContributor>(module, appRegistrationContributor));
            }

            if (module is IDebugManifestContributor debugManifestContributor)
            {
                debugManifest.Add(new ContributorRegistration<IDebugManifestContributor>(module, debugManifestContributor));
            }
        }

        var diagnostics = new List<DiagnosticInfo>();
        var orderedPostInitialization = OrderContributors(postInitialization, "post-initialization", diagnostics);
        var orderedDescriptor = OrderContributors(descriptor, "descriptor", diagnostics);
        var orderedExceptionTranslation = OrderContributors(exceptionTranslation, "exception-translation", diagnostics);
        var orderedPipeline = OrderContributors(pipeline, "pipeline", diagnostics);
        var orderedHost = OrderContributors(host, "host", diagnostics);
        var orderedAppRegistration = OrderContributors(appRegistration, "app-registration", diagnostics);
        var orderedDebugManifest = OrderContributors(debugManifest, "debug-manifest", diagnostics);

        if (diagnostics.Count > 0)
        {
            return Result<BlueprintModuleExecutionPlan>.Failure(diagnostics.ToArray());
        }

        return Result.Success(
            new BlueprintModuleExecutionPlan(
                orderedPostInitialization,
                orderedDescriptor,
                orderedExceptionTranslation,
                orderedPipeline,
                orderedHost,
                orderedAppRegistration,
                orderedDebugManifest));
    }

    /// <summary>
    /// Builds the validated pipeline plan for a specific operation using the configured modules.
    /// </summary>
    /// <param name="operationFullClassName">The fully-qualified metadata name of the operation type.</param>
    /// <param name="compilation">The active compilation used to resolve the operation symbol and related lookups.</param>
    /// <returns>The pipeline plan, or diagnostics if contributor composition or variable requirements are invalid.</returns>
    public Result<PipelinePlan> BuildPipelinePlan(string operationFullClassName, Compilation compilation)
    {
        return PipelinePlanBuilder.Build(operationFullClassName, compilation, this.BuildExecutionPlan());
    }

    /// <summary>
    /// Builds the per-operation debug manifest for a specific operation using the configured modules.
    /// </summary>
    /// <param name="operationFullClassName">The fully-qualified metadata name of the operation type.</param>
    /// <param name="compilation">The active compilation used to resolve the operation symbol and related lookups.</param>
    /// <returns>The debug manifest, or diagnostics if contributor composition is invalid.</returns>
    public Result<GeneratedDebugManifest> BuildDebugManifest(string operationFullClassName, Compilation compilation)
    {
        return DebugManifestPlanBuilder.Build(operationFullClassName, compilation, this.BuildExecutionPlan());
    }

    private static TContributor[] OrderContributors<TContributor>(
        IReadOnlyList<ContributorRegistration<TContributor>> registrations,
        string capabilityName,
        List<DiagnosticInfo> diagnostics)
        where TContributor : IBlueprintContributor
    {
        foreach (var duplicateGroup in registrations
                     .Where(r => r.Contributor.IsUnique)
                     .GroupBy(r => r.Contributor.ContributorId, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    FoundationModuleDiagnosticDescriptors.DuplicateUniqueContributor,
                    (Location?)null,
                    [
                        duplicateGroup.Key,
                        capabilityName,
                        string.Join(", ", duplicateGroup.Select(static r => r.Module.ModuleId).OrderBy(static id => id, StringComparer.Ordinal)),
                    ]));
        }

        var groupedById = registrations
            .GroupBy(r => r.Contributor.ContributorId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        var dependencyCounts = registrations.ToDictionary(r => r, _ => 0);
        var reverseDependencies = registrations.ToDictionary(r => r, _ => new List<ContributorRegistration<TContributor>>());

        foreach (var registration in registrations)
        {
            foreach (var dependencyId in registration.Contributor.DependsOn)
            {
                if (!groupedById.TryGetValue(dependencyId, out var dependencyMatches))
                {
                    diagnostics.Add(
                        new DiagnosticInfo(
                            FoundationModuleDiagnosticDescriptors.MissingContributorDependency,
                            (Location?)null,
                            [
                                registration.Contributor.ContributorId,
                                capabilityName,
                                dependencyId,
                            ]));
                    continue;
                }

                foreach (var dependency in dependencyMatches)
                {
                    dependencyCounts[registration]++;
                    reverseDependencies[dependency].Add(registration);
                }
            }
        }

        var ordered = new List<ContributorRegistration<TContributor>>(registrations.Count);
        var comparer = ContributorRegistrationComparer<TContributor>.Instance;

        // Use a sorted list maintained via binary-search insertion instead of re-sorting
        // the entire collection on every enqueue. RemoveAt(Count - 1) is O(1) vs RemoveAt(0)
        // which is O(n), so we sort in reverse order and pop from the end to get the
        // lowest-priority item first (matching the original ascending-order-first behavior).
        var reverseComparer = Comparer<ContributorRegistration<TContributor>>.Create(
            (x, y) => comparer.Compare(y, x));

        var ready = dependencyCounts
            .Where(kvp => kvp.Value == 0)
            .Select(kvp => kvp.Key)
            .OrderByDescending(r => r, comparer)
            .ToList();

        while (ready.Count > 0)
        {
            var next = ready[^1];
            ready.RemoveAt(ready.Count - 1);
            ordered.Add(next);

            foreach (var dependent in reverseDependencies[next])
            {
                dependencyCounts[dependent]--;

                if (dependencyCounts[dependent] == 0)
                {
                    var insertionIndex = ready.BinarySearch(dependent, reverseComparer);
                    ready.Insert(insertionIndex < 0 ? ~insertionIndex : insertionIndex, dependent);
                }
            }
        }

        if (ordered.Count != registrations.Count)
        {
            diagnostics.Add(
                new DiagnosticInfo(
                    FoundationModuleDiagnosticDescriptors.CyclicContributorDependency,
                    (Location?)null,
                    [
                        capabilityName,
                        string.Join(
                            ", ",
                            registrations
                                .Except(ordered)
                                .Select(r => r.Contributor.ContributorId)
                                .OrderBy(id => id, StringComparer.Ordinal)),
                    ]));
        }

        return ordered.Select(r => r.Contributor).ToArray();
    }

    private sealed record ContributorRegistration<TContributor>(
        IBlueprintSourceGeneratorModule Module,
        TContributor Contributor)
        where TContributor : IBlueprintContributor;

    private sealed class ContributorRegistrationComparer<TContributor> : IComparer<ContributorRegistration<TContributor>>
        where TContributor : IBlueprintContributor
    {
        public static ContributorRegistrationComparer<TContributor> Instance { get; } = new();

        public int Compare(ContributorRegistration<TContributor>? x, ContributorRegistration<TContributor>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var categoryComparison = x.Module.Category.CompareTo(y.Module.Category);

            if (categoryComparison != 0)
            {
                return categoryComparison;
            }

            var orderComparison = x.Contributor.Order.CompareTo(y.Contributor.Order);

            if (orderComparison != 0)
            {
                return orderComparison;
            }

            return StringComparer.Ordinal.Compare(x.Contributor.ContributorId, y.Contributor.ContributorId);
        }
    }
}
