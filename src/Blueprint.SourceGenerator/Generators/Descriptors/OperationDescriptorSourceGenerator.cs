using System.Text;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.ApiResources;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Modules.Auditing;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Modules.Authorization;
using Blueprint.SourceGenerator.Modules.LinkGeneration;
using Blueprint.SourceGenerator.Modules.Validation;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.Descriptors;

/// <summary>
/// Incremental source generator that emits operation descriptor implementations for Blueprint operations.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class OperationDescriptorSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private static readonly DiagnosticDescriptor _missingPartialModifierRule = new(
        id: "BP005",
        title: "Class must be partial",
        messageFormat: "The class '{0}' must be partial",
        category: nameof(OperationDescriptorSourceGenerator),
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private readonly BlueprintSourceGenerationConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationDescriptorSourceGenerator"/> class with Blueprint's default
    /// descriptor-module composition.
    /// </summary>
    public OperationDescriptorSourceGenerator()
        : this(CreateDefaultConfig())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationDescriptorSourceGenerator"/> class with an explicit module
    /// configuration. Companion generator packages can derive from this generator and extend the shared module set
    /// without copying descriptor-generation logic.
    /// </summary>
    /// <param name="config">The generator configuration to use.</param>
    protected OperationDescriptorSourceGenerator(BlueprintSourceGenerationConfig config)
    {
        this._config = config;
    }

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <summary>
    /// Configures syntax discovery, module execution, and descriptor emission for Blueprint operations.
    /// </summary>
    /// <param name="context">The incremental generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var executionPlanResult = this._config.BuildExecutionPlan();

        context.RegisterPostInitializationOutput(i =>
            {
                if (executionPlanResult.Value is null)
                {
                    return;
                }

                foreach (var contributor in executionPlanResult.Value.PostInitializationContributors)
                {
                    contributor.RegisterMarkerAttributes(i);
                }
            });

        var executionPlanProvider = context.ParseOptionsProvider
            .Select((_, _) => executionPlanResult)
            .WithTrackingName(Stages.ExecutionPlan);
        var exceptionTranslationPolicyProvider = executionPlanProvider
            .Select((result, _) => DescriptorPlanBuilder.CreateExceptionTranslatorPolicy(result))
            .WithTrackingName(Stages.ExceptionTranslationPolicy);

        context.RegisterSourceOutput(
            executionPlanProvider,
            (spc, result) => result.ReportDiagnostics(spc));

        var classProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax,
                transform: static (ctx, _) => Transform(ctx))
            .Where(static m => m != null)
            .Select(static (m, _) => m!)
            .WithTrackingName(Stages.InitialExtraction);

        var combinedProvider = classProvider.Combine(context.CompilationProvider)
            .WithComparer(CombinedProviderComparer<Result<OperationClassToGenerate>>.Instance)
            .WithTrackingName(Stages.CombineWithCompilation);

        var configuredModels = combinedProvider
            .Combine(executionPlanProvider)
            .Combine(exceptionTranslationPolicyProvider)
            .Select((x, _) => Configure(x.Left.Left, x.Left.Right, x.Right))
            .WithTrackingName(Stages.Configuration);

        context.RegisterSourceOutput(
            configuredModels,
            (spc, result) =>
            {
                result!.ReportDiagnostics(spc);

                if (result.Value is not { } classToGenerate)
                {
                    return;
                }

                spc.AddSource(
                    $"{classToGenerate.Operation.TypeName}_Descriptor.g.cs",
                    SourceText.From(GenerateDescriptorClass(classToGenerate), Encoding.UTF8));
            });
    }

    private static Result<OperationClassToGenerate>? Transform(GeneratorSyntaxContext ctx)
    {
        const string apiResourceSymbol = "Blueprint.Model.IOperation";

        var classNode = (ClassDeclarationSyntax)ctx.Node;
        var model = ctx.SemanticModel.GetDeclaredSymbol(classNode);

        if (model is null || !model.ImplementsInterface(apiResourceSymbol) || model.IsAbstract)
        {
            return null;
        }

        var isPartial = classNode.Modifiers.Any(SyntaxKind.PartialKeyword);

        return !isPartial
            ? Result<OperationClassToGenerate>.Failure(
                new DiagnosticInfo(_missingPartialModifierRule, classNode.Identifier.GetLocation(), [model.Name]))
            : Result.Success(
            new OperationClassToGenerate(
                model.ToString(),
                model.ContainingNamespace.ToDisplayString(),
                model.Name,
                classNode.ToFullString()));
    }

    private static Result<DescriptorPlan> Configure(
        (Result<OperationClassToGenerate> Result, Compilation Compilation) context,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        Result<PipelineExceptionTranslatorPolicy> exceptionTranslatorPolicyResult)
    {
        var (result, compilation) = context;

        if (result.Value is null)
        {
            return Result<DescriptorPlan>.Failure(result.Diagnostics);
        }

        return DescriptorPlanBuilder.Build(result.Value.FullClassName, compilation, executionPlanResult, exceptionTranslatorPolicyResult);
    }

    private static Result<PipelinePlan> ConfigurePipelinePlan(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult)
    {
        return PipelinePlanBuilder.Build(operationFullClassName, compilation, executionPlanResult);
    }

    private static string GenerateDescriptorClass(
        DescriptorPlan plan)
    {
        var descriptorClassName = plan.Operation.TypeName + "Descriptor";

        var codeBuilder = CodeBuilder.Create(plan.Operation.Namespace);

        var wrapperClass = codeBuilder.AddClass(plan.Operation.TypeName)
            .WithAccessModifier(Accessibility.Public);

        wrapperClass.AddMethod("Configure")
            .WithAccessModifier(Accessibility.Public)
            .MakeStaticMethod()
            .WithReturnType("ApiOperationDescriptor")
            .WithBody(c =>
                {
                    c.AppendLine($"return new {descriptorClassName}();");
                });

        // Note that we have to hack around Blueprint.SourceGenerator.CodeWriter a little to get a base call without constructor arguments. We
        // pass in an empty dictionary (as it *immediately* uses that to configure the parameters), and THEN after we
        // populate the dictionary because it uses callbacks to grab those when creating the base call.
        var baseParameters = new Dictionary<string, string>();

        wrapperClass.AddNestedClass(descriptorClassName)
            .WithAccessModifier(Accessibility.Private)
            .SetBaseClass("global::Blueprint.Model.ApiOperationDescriptor")
            .AddConstructor(Accessibility.Public)
            .WithBaseCall(baseParameters)
            .WithBody(plan.Render);

        // The keys do not matter, they are not used when generating the base call
        baseParameters.Add("type", "typeof(" + plan.Operation.TypeName + ")");
        baseParameters.Add("name", "\"" + plan.Operation.TypeName + "\"");
        baseParameters.Add("where", "\"Source Generated\"");

        return codeBuilder.Build();
    }

    /// <summary>
    /// Creates Blueprint's default descriptor-generator configuration and appends any additional modules after the built-in
    /// baseline so companion generator packages can extend the shared composition root.
    /// </summary>
    /// <param name="additionalModules">The additional modules to append.</param>
    /// <returns>The composed descriptor-generator configuration.</returns>
    protected internal static BlueprintSourceGenerationConfig CreateDefaultConfig(params IBlueprintSourceGeneratorModule[] additionalModules)
    {
        return new BlueprintSourceGenerationConfig(
        [
            new LinkGenerationModule(),
            new TasksHostModule(),
            new AuthenticationModule(),
            new AuthorizationModule(),
            new ValidationModule(),
            new DefaultExceptionTranslationModule(),
            new FlattenedResultOutcomeModule(),
            new FailureOutcomesModule(),
            new ResourceEventMetadataModule(),
            new HostExecutorAvailabilityModule(),
            new AmbiguousHostTranslationModule(),
            new NoResultShapeSeparationModule(),
            new HttpUnionDiscriminationStrategyModule(),
            new OperationHttpMethodMetadataModule(),
            new PropertyOwnershipModule(),
            new OperationGroupMetadataModule(),
            new HttpResponseMetadataModule(),
            new SerializerContextCoverageModule(),
            new SensitivePropertyMetadataModule(),
            new OperationDiscoveryManifestModule(),
            new AuthorizationManifestModule(),
            new LinkManifestModule(),
            new ValidationManifestModule(),
            .. additionalModules,
        ]);
    }

    /// <summary>
    /// Captures the operation identity needed to render a descriptor companion type.
    /// </summary>
    /// <param name="FullClassName">The fully-qualified operation type name.</param>
    /// <param name="Namespace">The namespace that will contain the generated descriptor wrapper.</param>
    /// <param name="ClassName">The simple operation type name.</param>
    /// <param name="DeclarationText">The operation declaration text used to invalidate semantic edits within the operation file.</param>
    private record OperationClassToGenerate(string FullClassName, string Namespace, string ClassName, string DeclarationText);

    /// <summary>
    /// The names of the <see cref="ApiResourceSourceGenerator"/> stages, used in particular to test for the
    /// cacheability of the generator.
    /// </summary>
    private static class Stages
    {
        public const string InitialExtraction = nameof(InitialExtraction);
        public const string ExecutionPlan = nameof(ExecutionPlan);
        public const string ExceptionTranslationPolicy = nameof(ExceptionTranslationPolicy);
        public const string CombineWithCompilation = nameof(CombineWithCompilation);
        public const string Configuration = nameof(Configuration);
    }
}
