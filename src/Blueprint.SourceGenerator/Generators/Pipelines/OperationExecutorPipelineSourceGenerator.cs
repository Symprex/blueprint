using System.Text;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Incremental source generator that discovers operation handlers and generates executor pipelines.
/// </summary>
[Generator(LanguageNames.CSharp)]
public partial class OperationExecutorPipelineSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private readonly BlueprintSourceGenerationConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationExecutorPipelineSourceGenerator"/> class with Blueprint's
    /// default pipeline-module composition.
    /// </summary>
    public OperationExecutorPipelineSourceGenerator()
        : this(CreateDefaultConfig())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationExecutorPipelineSourceGenerator"/> class with an explicit
    /// module configuration. Companion generator packages can derive from this generator and extend the shared pipeline
    /// module set without copying executor-generation logic.
    /// </summary>
    /// <param name="config">The generator configuration to use.</param>
    protected OperationExecutorPipelineSourceGenerator(BlueprintSourceGenerationConfig config)
    {
        this._config = config;
    }

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Stage 1: Find all BlueprintApp classes (they have BlueprintAppScan attributes)
        var appProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Blueprint.Model.BlueprintAppScanAttribute",
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, _) => ParseBlueprintApp(context)!)
            .WithTrackingName(Stages.FindBlueprintApp);

        // Stage 2: Combine with compilation to get operation types
        var combinedProvider = appProvider
            .Combine(context.CompilationProvider)
            .WithTrackingName(Stages.CombineWithCompilation);

        // Stage 3: Find handlers for all operations
        var handlersProvider = combinedProvider
            .Select((scanModelContext, _) => (Compilation: scanModelContext.Right, Result: FindHandlersForOperations(scanModelContext)))
            .WithTrackingName(Stages.FindHandlers);

        // Stage 4: Generate pipeline classes for each operation
        context.RegisterSourceOutput(
            handlersProvider,
            (spc, context) =>
            {
                var compilation = context.Compilation;
                var result = context.Result;
                result!.ReportDiagnostics(spc);

                if (result.Value is not { } compilationModel)
                {
                    return;
                }

                var executionPlanResult = this._config.BuildExecutionPlan();
                executionPlanResult.ReportDiagnostics(spc);

                if (executionPlanResult.Value is null)
                {
                    return;
                }

                var exceptionTranslatorPolicyResult = CreateExceptionTranslatorPolicy(executionPlanResult);
                exceptionTranslatorPolicyResult.ReportDiagnostics(spc);

                if (exceptionTranslatorPolicyResult.Value is null)
                {
                    return;
                }

                var successfulHandlers = new List<OperationHandlerModel>();

                // Generate a pipeline for each handler
                foreach (var handler in compilationModel.Handlers)
                {
                    var pipelinePlanResult = PipelinePlanBuilder.Build(
                        handler.OperationTypeFullName,
                        compilation,
                        executionPlanResult,
                        handler);
                    pipelinePlanResult.ReportDiagnostics(spc);

                    if (pipelinePlanResult.Value is null)
                    {
                        continue;
                    }

                    successfulHandlers.Add(handler);

                    var pipelineSource = PipelineCodeGenerator.GeneratePipeline(handler, pipelinePlanResult.Value);
                    spc.AddSource(
                        $"{handler.OperationTypeName}ExecutorPipeline.g.cs",
                        SourceText.From(pipelineSource, Encoding.UTF8));
                }

                if (successfulHandlers.Count == 0)
                {
                    return;
                }

                // Generate the executor with all registrations
                var executorSource = ExecutorCodeGenerator.GenerateExecutor(
                    compilationModel with
                    {
                        Handlers = new EquatableList<OperationHandlerModel>(successfulHandlers.ToArray()),
                    });
                spc.AddSource(
                    $"{compilationModel.AppTypeName}Executor.g.cs",
                    SourceText.From(executorSource, Encoding.UTF8));
            });
    }

    /// <summary>
    /// Creates Blueprint's default pipeline-generator configuration and appends any additional modules after the built-in
    /// baseline so companion generator packages can extend the shared composition root.
    /// </summary>
    /// <param name="additionalModules">The additional modules to append.</param>
    /// <returns>The composed pipeline-generator configuration.</returns>
    protected static BlueprintSourceGenerationConfig CreateDefaultConfig(params IBlueprintSourceGeneratorModule[] additionalModules)
    {
        return new BlueprintSourceGenerationConfig(
        [
            new PropertyOwnershipModule(),
            new AuthenticationModule(),
            new ValidationModule(),
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new BackgroundTaskFlushModule(),
            new LinkGenerationModule(),
            new PipelineReturnModule(),
            new DefaultExceptionTranslationModule(),
            .. additionalModules,
        ]);
    }

    private static Result<PipelineExceptionTranslatorPolicy> CreateExceptionTranslatorPolicy(Result<BlueprintModuleExecutionPlan> executionPlanResult)
    {
        return executionPlanResult.Value is null
            ? Result<PipelineExceptionTranslatorPolicy>.Failure(executionPlanResult.Diagnostics)
            : PipelineExceptionTranslatorPolicy.CreateFromContributors(executionPlanResult.Value.ExceptionTranslationContributors);
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
            return other is null
                ? false
                : ReferenceEquals(this, other)
                ? true
                : this.AppTypeName == other.AppTypeName &&
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
