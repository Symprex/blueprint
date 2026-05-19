using System.Text;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Generators.App;

/// <summary>
/// Incremental source generator that discovers Blueprint app scan declarations and emits app registrations.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class AppDataModelSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private static readonly DiagnosticDescriptor _missingPartialModifierRule = new(
        id: "BP002",
        title: "Class must be partial",
        messageFormat: "The class '{0}' must be partial to generate data model registrations",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _noMatchingTypesFound = new(
        "BP003",
        "No matching types found",
        "There are no types matching attribute's search criteria",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor _cannotParseScanAttribute = new(
        id: "BP004",
        title: "Cannot parse Scan attribute",
        messageFormat: "The Scan attribute is not valid",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _serializerContextMissingCoverageRule = new(
        id: "BP006",
        title: "Serializer context is missing required response body coverage",
        messageFormat: "The declared app-owned serializer context '{0}' does not cover required HTTP response body types {1} for operations {2}",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HTTP-visible Blueprint operations must have their response body payload types covered by the app-owned JsonSerializerContext declaration before OpenAPI/schema generation can trust the compile-time contract.");

    private static readonly DiagnosticDescriptor _missingSerializerContextDeclarationRule = new(
        id: "BP007",
        title: "App is missing a serializer context declaration",
        messageFormat: "The Blueprint app '{0}' exposes HTTP response body payload types {1} for operations {2} but does not declare [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(...))]",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HTTP-visible Blueprint apps must declare an app-owned JsonSerializerContext before OpenAPI/schema generation can trust the compile-time serializer contract.");

    private static readonly DiagnosticDescriptor _unsupportedOpenApiValidationShapeRule = new(
        id: "BP008",
        title: "Serializer-covered body type uses unsupported OpenAPI validation shape",
        messageFormat: "The declared app-owned serializer context '{0}' covers HTTP response body type '{1}', but property '{2}' uses validation attribute '{3}', which the current OpenAPI schema projection cannot represent",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "OpenAPI schema projection currently supports local per-property validation constraints only. Covered serializer-backed body types that use cross-property validation must fail during generation instead of silently producing incomplete OpenAPI contracts.");

    private static readonly DiagnosticDescriptor _duplicateStrippedDescriptorNameRule = new(
        id: "BP009",
        title: "Duplicate descriptor name after Query/Command suffix stripping",
        messageFormat: "Operations {0} produce the same descriptor name '{1}' after stripping Query/Command suffixes. Rename one of them to avoid an OpenAPI operationId collision.",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Blueprint strips 'Query' and 'Command' suffixes from operation type names to produce descriptor names that flow through to OpenAPI operationId values. When two operations produce the same stripped name (e.g. FooQuery and FooCommand both become Foo), the collision must be resolved by renaming one of the operations.");

    private static readonly DiagnosticDescriptor _crossOperationRouteCollisionRule = new(
        id: "BPLG007",
        title: "Cross-operation route collision",
        messageFormat: "Operations '{0}' and '{1}' both declare route '{2}' with HTTP method {3}. Both endpoints will be registered, causing an AmbiguousMatchException at runtime when a request matches this route.",
        category: "LinkGeneration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Two different operations declare the same route template and HTTP method combination via link attributes. ASP.NET Core will register both endpoints, but requests matching the route will fail with an AmbiguousMatchException because the router cannot disambiguate between them. Rename one route or consolidate the operations.");

    private readonly BlueprintSourceGenerationConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDataModelSourceGenerator"/> class with Blueprint's default
    /// descriptor-module composition so app registration and descriptor-derived validation reuse the same contributor set.
    /// </summary>
    public AppDataModelSourceGenerator()
        : this(OperationDescriptorSourceGenerator.CreateDefaultConfig())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDataModelSourceGenerator"/> class with an explicit module
    /// configuration. Tests and companion packages can reuse the app generator while keeping descriptor-derived
    /// validation aligned with the configured module set.
    /// </summary>
    /// <param name="config">The generator configuration to use.</param>
    internal AppDataModelSourceGenerator(BlueprintSourceGenerationConfig config)
    {
        this._config = config;
    }

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var executionPlanProvider = context.ParseOptionsProvider
            .Select((_, _) => this._config.BuildExecutionPlan())
            .WithTrackingName(Stages.ExecutionPlan);
        var exceptionTranslationPolicyProvider = executionPlanProvider
            .Select((result, _) => DescriptorPlanBuilder.CreateExceptionTranslatorPolicy(result))
            .WithTrackingName(Stages.ExceptionTranslationPolicy);

        context.RegisterSourceOutput(
            executionPlanProvider,
            (spc, result) => result.ReportDiagnostics(spc));

        var classProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Blueprint.Model.BlueprintAppScanAttribute",
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, _) => ParseDataModelWithScanAttributes(context)!)
            .Where(method => method != null)
            .WithTrackingName(Stages.InitialExtraction);

        var combinedProvider = classProvider.Combine(context.CompilationProvider)
            .WithComparer(CombinedProviderComparer<Result<ParseOnlyDataModel>>.Instance)
            .WithTrackingName(Stages.CombineWithCompilation);

        var generationModelProvider = combinedProvider
            .Combine(executionPlanProvider)
            .Combine(exceptionTranslationPolicyProvider)
            .Select((context, _) => FindOperations(context.Left.Left, context.Left.Right, context.Right))
            .WithTrackingName(Stages.FindServicesToRegister);

        context.RegisterSourceOutput(
            generationModelProvider,
            (spc, result) =>
            {
                result!.ReportDiagnostics(spc);

                if (result.Value is not { } classToGenerate)
                {
                    return;
                }

                foreach (var diagnostic in classToGenerate.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                spc.AddSource(
                    $"{classToGenerate.Class.Name}_DataModelRegistration.g.cs",
                    SourceText.From(GenerateRegistrations(classToGenerate), Encoding.UTF8));
            });
    }

    /// <summary>
    /// Renders the generated constructor body that registers each discovered operation descriptor with the Blueprint app.
    /// </summary>
    /// <param name="compilationModel">The app compilation model that contains the discovered operations to register.</param>
    /// <returns>The generated registration companion source.</returns>
    private static string GenerateRegistrations(BlueprintAppDataModelCompilationModel compilationModel)
    {
        var codeBuilder = CodeBuilder.Create(compilationModel.Class.Namespace!);
        var classBuilder = codeBuilder.AddClass(compilationModel.Class.Name)
            .WithAccessModifier(Accessibility.Public);

        classBuilder.AddConstructor()
            .WithBody(c =>
            {
                if (compilationModel.Class.SerializerContextTypeName is { } serializerContextTypeName)
                {
                    c.AppendLine($"this.AppOwnedSerializerContext = new {serializerContextTypeName}();");
                    c.AppendLine($"this.AppOwnedSerializerContextType = typeof({serializerContextTypeName});");
                    if (compilationModel.Class.SerializerContextBodyTypeTypeNames.Count > 0)
                    {
                        c.AppendLine($"this.AppOwnedSerializerContextBodyTypes = {RenderTypeArray(compilationModel.Class.SerializerContextBodyTypeTypeNames)};");
                    }

                    if (compilationModel.Class.SerializerContextBodyTypeNames.Count > 0)
                    {
                        c.AppendLine($"this.AppOwnedSerializerContextBodyTypeNames = {RenderStringArray(compilationModel.Class.SerializerContextBodyTypeNames)};");
                    }
                }

                foreach (var registration in compilationModel.Operations)
                {
                    c.AppendLine($"this.Register({registration}.Configure());");
                }

                c.AppendLine(string.Empty);
                c.AppendLine("this.Freeze();");
            });

        // Generate RegisterLinkCheckers method if any StateBasedLinkChecker subclasses were discovered
        if (compilationModel.LinkCheckerTypes.Count > 0)
        {
            classBuilder.AddMethod("RegisterLinkCheckers", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder", "builder")
                .WithBody(c =>
                {
                    foreach (var checkerType in compilationModel.LinkCheckerTypes)
                    {
                        c.AppendLine($"builder.AddLinkVisibilityAuthoriser<global::{checkerType}>();");
                    }
                });
        }

        return codeBuilder.Build();
    }

    private static string RenderStringArray(IEnumerable<string> values)
    {
        var renderedValues = values
            .Select(static value => CSharpSymbolDisplay.FormatLiteral(value, quote: true))
            .ToArray();

        return renderedValues.Length == 0
            ? "global::System.Collections.Immutable.ImmutableArray<string>.Empty"
            : $"global::System.Collections.Immutable.ImmutableArray.Create<string>({string.Join(", ", renderedValues)})";
    }

    private static string RenderTypeArray(IEnumerable<string> values)
    {
        var renderedValues = values
            .Select(static value => $"typeof({value})")
            .ToArray();

        return renderedValues.Length == 0
            ? "global::System.Collections.Immutable.ImmutableArray<global::System.Type>.Empty"
            : $"global::System.Collections.Immutable.ImmutableArray.Create<global::System.Type>({string.Join(", ", renderedValues)})";
    }

    private static class Stages
    {
        /// <summary>
        /// Tracks construction of the shared descriptor-module execution plan reused for app validation.
        /// </summary>
        public const string ExecutionPlan = nameof(ExecutionPlan);

        /// <summary>
        /// Tracks the point where the shared exception-translation policy is derived for descriptor-backed app validation.
        /// </summary>
        public const string ExceptionTranslationPolicy = nameof(ExceptionTranslationPolicy);

        /// <summary>
        /// Tracks syntax discovery for candidate Blueprint app declarations.
        /// </summary>
        public const string InitialExtraction = nameof(InitialExtraction);

        /// <summary>
        /// Tracks the point where parsed scan metadata is paired with the Roslyn compilation.
        /// </summary>
        public const string CombineWithCompilation = nameof(CombineWithCompilation);

        /// <summary>
        /// Tracks assembly scanning and operation-registration discovery.
        /// </summary>
        public const string FindServicesToRegister = nameof(FindServicesToRegister);
    }
}


