using System.Text;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Incremental source generator that discovers operation handlers and generates executor pipelines.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class OperationExecutorPipelineSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Stage 1: Find all BlueprintApp classes (they have BlueprintAppScan attributes)
        var appProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Blueprint.Model.BlueprintAppScanAttribute",
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, _) => ParseBlueprintApp(context))
            .Where(method => method != null)
            .WithTrackingName(Stages.FindBlueprintApp);

        // Stage 2: Combine with compilation to get operation types
        var combinedProvider = appProvider.Combine(context.CompilationProvider)
            .WithComparer(CombinedProviderComparer<Result<AppInfo>>.Instance)
            .WithTrackingName(Stages.CombineWithCompilation);

        // Stage 3: Find handlers for all operations
        var handlersProvider = combinedProvider
            .Select((scanModelContext, _) => FindHandlersForOperations(scanModelContext))
            .WithTrackingName(Stages.FindHandlers);

        // Stage 4: Generate pipeline classes for each operation
        context.RegisterSourceOutput(
            handlersProvider,
            (spc, result) =>
            {
                result!.ReportDiagnostics(spc);

                if (result.Value is not { } compilationModel)
                {
                    return;
                }

                // Generate a pipeline for each handler
                foreach (var handler in compilationModel.Handlers)
                {
                    var pipelineSource = PipelineCodeGenerator.GeneratePipeline(handler);
                    spc.AddSource(
                        $"{handler.OperationTypeName}ExecutorPipeline.g.cs",
                        SourceText.From(pipelineSource, Encoding.UTF8));
                }

                // Generate the executor with all registrations
                var executorSource = ExecutorCodeGenerator.GenerateExecutor(compilationModel);
                spc.AddSource(
                    $"{compilationModel.AppTypeName}Executor.g.cs",
                    SourceText.From(executorSource, Encoding.UTF8));
            });
    }

    /// <summary>
    /// Model representing basic info about a BlueprintApp.
    /// </summary>
    private sealed record AppInfo(
        string AppTypeName,
        string AppNamespace,
        INamedTypeSymbol AppType) : IEquatable<AppInfo>
    {
        public bool Equals(AppInfo? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.AppTypeName == other.AppTypeName &&
                   this.AppNamespace == other.AppNamespace &&
                   SymbolEqualityComparer.Default.Equals(this.AppType, other.AppType);
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(
                this.AppTypeName,
                this.AppNamespace,
                SymbolEqualityComparer.Default.GetHashCode(this.AppType));
        }
    }

    /// <summary>
    /// Stage names for tracking generator performance.
    /// </summary>
    private static class Stages
    {
        public const string FindBlueprintApp = nameof(FindBlueprintApp);
        public const string CombineWithCompilation = nameof(CombineWithCompilation);
        public const string FindHandlers = nameof(FindHandlers);
    }
}
