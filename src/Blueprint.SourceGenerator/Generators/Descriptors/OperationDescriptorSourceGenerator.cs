using System.Text;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.ApiResources;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Modules.Auditing;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Modules.Authorization;
using Blueprint.SourceGenerator.Modules.BackgroundTaskFlush;
using Blueprint.SourceGenerator.Modules.Gateway;
using Blueprint.SourceGenerator.Modules.HttpClient;
using Blueprint.SourceGenerator.Modules.LinkGeneration;
using Blueprint.SourceGenerator.Modules.Messaging;
using Blueprint.SourceGenerator.Modules.OpenApi;
using Blueprint.SourceGenerator.Modules.Persistence;
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
            (spc, result) => result.ReportDiagnostics(spc, compilation: null));

        var classProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => Transform(ctx))
            .Where(static m => m != null)
            .Select(static (m, _) => m!)
            .WithTrackingName(Stages.InitialExtraction);

        // The plan reads every partial declaration and base type, but the syntax provider re-runs Transform only for an
        // edited file, so each run re-fingerprints them here, where the compilation is visible, before the plan is reused.
        var combinedProvider = classProvider.Combine(context.CompilationProvider)
            .Select(static (x, ct) => (Model: WithDeclarationChecksums(x.Left, x.Right, ct), Compilation: x.Right))
            .WithComparer(CombinedProviderComparer<Result<OperationClassToGenerate>>.Instance)
            .WithTrackingName(Stages.CombineWithCompilation);

        var configuredModels = combinedProvider
            .Combine(executionPlanProvider)
            .Combine(exceptionTranslationPolicyProvider)
            .Select((x, _) => Configure(x.Left.Left, x.Left.Right, x.Right))
            .WithTrackingName(Stages.Configuration);

        context.RegisterDiagnosticOutput(configuredModels.Select(static (result, _) => result!.Diagnostics));

        // D9/D25: an [Expose<TInner>] class's contributed properties, emitted into its partial whether or not its descriptor
        // can be planned, so a failing plan does not cascade into errors for every read of them. Every generator reads the
        // same properties from OperationPropertyModelBuilder, so none needs to see this output.
        var allowExpose = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => !(options.GlobalOptions.TryGetValue("build_property.BlueprintGatewayAllowExpose", out var value)
                && string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase)))
            .WithTrackingName(Stages.ExposePolicy);

        var exposedOperations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OperationPropertyModelBuilder.ExposeAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ((INamedTypeSymbol)ctx.TargetSymbol).ToFullMetadataName())
            .Combine(context.CompilationProvider)
            .Select(static (x, _) => ExposedOperationAnalysis.Analyse(x.Left, x.Right))
            .Where(static analysis => analysis is not null)
            .Select(static (analysis, _) => analysis!)
            .WithTrackingName(Stages.ExposedOperations);

        context.RegisterDiagnosticOutput(exposedOperations
            .Combine(allowExpose)
            .Select(static (x, _) => x.Left.GetDiagnostics(allowExpose: x.Right)));

        // D10/D16: a [ForwardsTo<TInner>] class's binding, type-match and supply diagnostics. It declares its properties
        // itself, so nothing is emitted for it.
        var forwardedOperations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclaredForward.ForwardsToAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ((INamedTypeSymbol)ctx.TargetSymbol).ToFullMetadataName())
            .Combine(context.CompilationProvider)
            .Select(static (x, _) => DeclaredForwardAnalysis.Analyse(x.Left, x.Right))
            .Where(static analysis => analysis is not null)
            .Select(static (analysis, _) => analysis!.Diagnostics)
            .WithTrackingName(Stages.ForwardedOperations);

        context.RegisterDiagnosticOutput(forwardedOperations);

        context.RegisterSourceOutput(
            exposedOperations.Where(static analysis => analysis.Model.HasContributedProperties),
            static (spc, analysis) => spc.AddSource(
                $"{analysis.Model.OperationTypeName}_ExposedProperties.g.cs",
                SourceText.From(OperationPropertyModelBuilder.RenderContributedProperties(analysis.Model), Encoding.UTF8)));

        context.RegisterSourceOutput(
            configuredModels,
            (spc, result) =>
            {
                if (result!.Value is not { } classToGenerate)
                {
                    return;
                }

                spc.AddSource(
                    GeneratedNamespace.Qualify(classToGenerate.Operation.Namespace, $"{classToGenerate.Operation.TypeName}_Descriptor.g.cs"),
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
                classNode.ToFullString(),
                HttpClientRequestBuilderModule.CanDeclareHttpClass(model, ctx.SemanticModel.Compilation)));
    }

    private static Result<OperationClassToGenerate> WithDeclarationChecksums(
        Result<OperationClassToGenerate> result,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (result.Value is not { } operation)
        {
            return result;
        }

        var checksums = new List<string>();

        // A metadata base type has no declarations, and nothing above it can be declared in this compilation.
        for (var current = compilation.GetTypeByMetadataName(operation.FullClassName);
             current is { DeclaringSyntaxReferences.Length: > 0 };
             current = current.BaseType)
        {
            foreach (var reference in current.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The checksum is cached on the file's text, which an unedited file keeps, so this costs nothing for it.
                var checksum = reference.SyntaxTree.GetText(cancellationToken).GetChecksum();
                checksums.Add(reference.SyntaxTree.FilePath + ":" + Convert.ToBase64String(checksum.ToArray()));
            }
        }

        // D25: an exposed operation's descriptor depends on its inner operation, which may be in another file or assembly,
        // where no checksum above reaches (a rebuilt contract assembly has no file here), so its property model is part of
        // the key too. A declared-mode operation's model carries the rules and docs it takes from its inner operation (D10).
        var type = compilation.GetTypeByMetadataName(operation.FullClassName);
        var exposedProperties = type is not null && OperationPropertyModelBuilder.FindGatewayInnerType(type, out _) is not null
            ? OperationPropertyModelBuilder.Build(type)
            : null;

        // D7, D8: the credential strategy and translation factories the operation names can be declared in other files.
        var forwardPolicyDiagnostics = type is null
            ? EquatableList<DiagnosticInfo>.Empty
            : EquatableList<DiagnosticInfo>.TakeOwnership(UpstreamForwardPolicyAnalysis.AnalyseOperation(type).ToList());

        return result with
        {
            Value = operation with
            {
                DeclarationChecksums = EquatableList<string>.TakeOwnership(checksums),
                ExposedProperties = exposedProperties,
                ForwardPolicyDiagnostics = forwardPolicyDiagnostics,
            },
        };
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

    private static string GenerateDescriptorClass(
        DescriptorPlan plan)
    {
        var descriptorClassName = plan.Operation.TypeName + "Descriptor";

        var codeBuilder = plan.Operation.Namespace.Length == 0
            ? CodeBuilder.CreateInGlobalNamespace()
            : CodeBuilder.Create(plan.Operation.Namespace);

        var wrapperClass = codeBuilder.AddClass(plan.Operation.TypeName)
            .WithAccessModifier(Accessibility.Public);

        // When the operation inherits from a concrete base that also implements IOperation,
        // the base type will have its own generated Configure(). Use 'new' to avoid CS0108.
        var baseHasConfigure = BaseTypeHasConfigureMethod(plan.Operation.TypeSymbol);

        var configureMethod = wrapperClass.AddMethod("Configure")
            .WithAccessModifier(Accessibility.Public)
            .MakeStaticMethod()
            .WithReturnType("ApiOperationDescriptor")
            .WithBody(c =>
                {
                    c.AppendLine($"return new {descriptorClassName}();");
                });

        if (baseHasConfigure)
        {
            configureMethod.MakeNewMethod();
        }

        plan.RenderOperationMembers(wrapperClass);

        // Note that we have to hack around Blueprint.SourceGenerator.CodeWriter a little to get a base call without constructor arguments. We
        // pass in an empty dictionary (as it *immediately* uses that to configure the parameters), and THEN after we
        // populate the dictionary because it uses callbacks to grab those when creating the base call.
        var baseParameters = new Dictionary<string, string>();

        wrapperClass.AddNestedClass(descriptorClassName)
            .WithAccessModifier(Accessibility.Private)
            .SetBaseClass("global::Blueprint.Model.ApiOperationDescriptor")
            .AddConstructor(Accessibility.Public)
            .WithBaseCall(baseParameters)
            .WithBody(c =>
            {
                plan.Render(c);

                // Emit a compiled instance factory to avoid Activator.CreateInstance on every request.
                // For types with `required` members, fall back to Activator which bypasses the requirement.
                if (HasRequiredMembers(plan.Operation.TypeSymbol))
                {
                    c.AppendLine($"this.InstanceFactory = static () => System.Activator.CreateInstance(typeof({plan.Operation.TypeName}))!;");
                }
                else
                {
                    c.AppendLine($"this.InstanceFactory = static () => new {plan.Operation.TypeName}();");
                }
            });

        // The keys do not matter, they are not used when generating the base call
        baseParameters.Add("type", "typeof(" + plan.Operation.TypeName + ")");
        baseParameters.Add("name", "\"" + OperationNameHelpers.GetDescriptorName(plan.Operation.TypeName) + "\"");
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
            new ApiExceptionFactoryModule(),
            new ResourceEventMetadataModule(),
            new HostExecutorAvailabilityModule(),
            new AmbiguousHostTranslationModule(),
            new NoResultShapeSeparationModule(),
            new HttpUnionDiscriminationStrategyModule(),
            new OperationHttpMethodMetadataModule(),
            new PropertyOwnershipModule(),
            new HttpClientRequestBuilderModule(),
            new OperationGroupMetadataModule(),
            new OperationDocumentationModule(),
            new HttpResponseMetadataModule(),
            new SerializerContextCoverageModule(),
            new SensitivePropertyMetadataModule(),
            new OperationDiscoveryManifestModule(),
            new AuthorizationManifestModule(),
            new LinkManifestModule(),
            new ValidationManifestModule(),
            new PersistenceManifestModule(),
            new MessageOutboxManifestModule(),
            new BackgroundTaskFlushManifestModule(),
            new ResourceEventManifestModule(),
            new ResultContractManifestModule(),
            new HttpMethodManifestModule(),
            new SensitivePropertyManifestModule(),
            new OperationGroupManifestModule(),
            new GatewayModule(),
            .. additionalModules,
        ]);
    }

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        // A contributed property is emitted required when the inner one is (D25), which this compilation cannot see.
        if (OperationPropertyModelBuilder.GetProperties(type).Any(static p => p.Info is { Origin: OperationPropertyOrigin.Contributed, IsRequired: true }))
        {
            return true;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether any base type of the operation already declares — or will have generated —
    /// a static parameterless <c>Configure()</c> method. This covers both cross-assembly bases
    /// (where <c>Configure()</c> is already compiled into the referenced assembly) and same-assembly
    /// bases (where the generator will emit <c>Configure()</c> in the same pass, but the symbol
    /// is not yet visible). When true, the derived descriptor must emit <c>new static</c> to avoid CS0108.
    /// </summary>
    private static bool BaseTypeHasConfigureMethod(INamedTypeSymbol operationType)
    {
        const string operationInterface = "Blueprint.Model.IOperation";

        for (var current = operationType.BaseType; current is not null; current = current.BaseType)
        {
            // Cross-assembly case: the base type already has a compiled Configure() from a previous generation.
            foreach (var member in current.GetMembers("Configure"))
            {
                if (member is IMethodSymbol { IsStatic: true, Parameters.Length: 0 })
                {
                    return true;
                }
            }

            // Same-assembly case: the base is a concrete IOperation declared in the current compilation,
            // so this generator will produce Configure() for it in the same pass even though the symbol
            // doesn't exist yet. Only applies to source-declared types (not metadata references from
            // assemblies that may not have the generator active).
            if (!current.IsAbstract
                && current.DeclaringSyntaxReferences.Length > 0
                && current.ImplementsInterface(operationInterface))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Captures the operation identity needed to render a descriptor companion type.
    /// </summary>
    /// <param name="FullClassName">The fully-qualified operation type name.</param>
    /// <param name="Namespace">The namespace that will contain the generated descriptor wrapper.</param>
    /// <param name="ClassName">The simple operation type name.</param>
    /// <param name="DeclarationText">The operation declaration text used to invalidate semantic edits within the operation file.</param>
    /// <param name="CanDeclareHttpClass">
    /// Whether the operation's partial can declare <see cref="HttpClientRequestBuilderModule"/>'s nested <c>Http</c> class,
    /// which turns on every partial declaration and base type of the operation, and on what its own uses of <c>Http</c> bind
    /// to (an alias another file declares, say), rather than on this declaration alone. Without
    /// it, an <c>Http</c> member added in another file left the class of a cached plan beside it (CS0102) until a clean build.
    /// </param>
    private record OperationClassToGenerate(string FullClassName, string Namespace, string ClassName, string DeclarationText, bool CanDeclareHttpClass)
    {
        /// <summary>
        /// Gets the path and checksum of each file declaring the operation or one of its source-declared base types. The
        /// plan reads properties and attributes from all of them, so an edit to any one of them — another partial
        /// declaration, or a base type — re-plans the descriptor rather than replaying the plan cached for
        /// <see cref="DeclarationText"/>, whose file the syntax provider does not revisit while it is unedited.
        /// </summary>
        public EquatableList<string> DeclarationChecksums { get; init; } = EquatableList<string>.Empty;

        /// <summary>
        /// Gets the property model of an operation that exposes another with <c>[Expose&lt;TInner&gt;]</c>, so an edit to
        /// the inner operation re-plans the descriptor although no file of the operation changed.
        /// </summary>
        public OperationPropertyModel? ExposedProperties { get; init; }

        /// <summary>
        /// Gets what <see cref="UpstreamForwardPolicyAnalysis"/> reports for the operation, so an edit to the credential
        /// strategy or a translation factory it names, in another file, re-plans the descriptor that reports it.
        /// </summary>
        public EquatableList<DiagnosticInfo> ForwardPolicyDiagnostics { get; init; } = EquatableList<DiagnosticInfo>.Empty;
    }

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
        public const string ExposePolicy = nameof(ExposePolicy);
        public const string ExposedOperations = nameof(ExposedOperations);
        public const string ForwardedOperations = nameof(ForwardedOperations);
    }
}
