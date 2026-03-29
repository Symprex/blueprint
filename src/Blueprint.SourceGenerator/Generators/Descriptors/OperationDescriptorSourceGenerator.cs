using System.Text;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.ApiResources;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
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
public sealed class OperationDescriptorSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private static readonly DiagnosticDescriptor _missingPartialModifierRule = new(
        id: "BP005",
        title: "Class must be partial",
        messageFormat: "The class '{0}' must be partial",
        category: nameof(OperationDescriptorSourceGenerator),
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <summary>
    /// Configures syntax discovery, module execution, and descriptor emission for Blueprint operations.
    /// </summary>
    /// <param name="context">The incremental generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var config = new BlueprintSourceGenerationConfig([new LinkGenerationModule(), new TasksHostModule(), new ValidationModule(), new FlattenedResultOutcomeModule(), new HostExecutorAvailabilityModule(), new AmbiguousHostTranslationModule(), new OperationDiscoveryManifestModule()]);
        var executionPlanResult = config.BuildExecutionPlan();

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
            .Select((x, _) => Configure(x.Left, x.Right))
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
        Result<BlueprintModuleExecutionPlan> executionPlanResult)
    {
        var (result, compilation) = context;

        if (result.Value is null)
        {
            return Result<DescriptorPlan>.Failure(result.Diagnostics);
        }

        return ConfigureDescriptorPlan(result.Value.FullClassName, compilation, executionPlanResult);
    }

    private static Result<DescriptorPlan> ConfigureDescriptorPlan(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult)
    {
        if (executionPlanResult.Value is null)
        {
            return Result<DescriptorPlan>.Failure(executionPlanResult.Diagnostics);
        }

        var operationImplementationType = compilation.GetTypeByMetadataName(operationFullClassName);
        var operationModel = OperationModel.Create(operationImplementationType!);
        var descriptorPlan = new DescriptorPlan(operationModel);
        var operationLookup = new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlanResult.Value.DescriptorContributors)
        {
            contributor.ContributeToDescriptor(new DescriptorContributorContext(operationModel, descriptorPlan, operationLookup));
        }

        HostExposureDiagnostics.AddUnsupportedHostDiagnostics(descriptorPlan, executionPlanResult.Value.HostContributors);
        HostExposureDiagnostics.AddConflictingHostDiagnostics(descriptorPlan, executionPlanResult.Value.HostContributors);

        var pipelinePlanResult = ConfigurePipelinePlan(operationFullClassName, compilation, executionPlanResult);
        var failures = descriptorPlan.Diagnostics
            .Concat(pipelinePlanResult.Diagnostics)
            .ToArray();

        return failures.Length > 0 ? Result<DescriptorPlan>.Failure(failures) : Result.Success(descriptorPlan);
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
        public const string CombineWithCompilation = nameof(CombineWithCompilation);
        public const string Configuration = nameof(Configuration);
    }
}
