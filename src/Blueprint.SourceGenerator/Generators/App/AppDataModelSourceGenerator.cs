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
    private const string ApplyOperationNameTransformMethodName = "ApplyOperationNameTransform";

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
        title: "Duplicate descriptor name once Query/Command is removed and the name is cased",
        messageFormat: "Operations {0} produce the same descriptor name '{1}' once 'Query' and 'Command' are removed and the name is cased. Rename one of them to avoid an OpenAPI operationId collision.",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Blueprint names an operation as legacy Blueprint did: 'Query' and then 'Command' are removed wherever they occur in the type name, and the result is PascalCased with acronyms folded (GenerateVCardQuery becomes GeneratevCard). The name flows through to OpenAPI operationId values. When two operations produce the same name (e.g. FooQuery and FooCommand both become Foo, and GetVCardQuery and GetvCardCommand both become GetvCard), the collision must be resolved by renaming one of the operations.");

    private static readonly DiagnosticDescriptor _duplicateRemovedDescriptorNameRule = new(
        id: "BP011",
        title: "Duplicate descriptor name after the app's operation-name removal",
        messageFormat: "Operations {0} produce the same descriptor name '{1}' after the app removes '{2}' from operation names. Rename one of them, or change the app's [BlueprintAppOperationNames], to avoid an OpenAPI operationId collision.",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[BlueprintAppOperationNames(Remove = ...)] removes text from the descriptor name of every operation the app hosts, once Query/Command has been removed and the name cased. When that gives two operations the same name, their OpenAPI operationIds, request-body schema keys and link operation names collide, so the collision must be resolved by renaming an operation or changing the removal.");

    // BP011's second form: a removal that leaves a name empty is the degenerate case of the same misconfiguration.
    private static readonly DiagnosticDescriptor _emptyRemovedDescriptorNameRule = new(
        id: "BP011",
        title: "Empty descriptor name after the app's operation-name removal",
        messageFormat: "The app's removal of '{0}' from operation names leaves {1} with an empty descriptor name. Rename it, or change the app's [BlueprintAppOperationNames], so its OpenAPI operationId is not empty.",
        category: "AppDataModelSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[BlueprintAppOperationNames(Remove = ...)] removes text from the descriptor name of every operation the app hosts, once Query/Command has been removed and the name cased. When that removes the whole name, the operation has an empty OpenAPI operationId and request-body schema key, so the operation must be renamed or the removal changed.");

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

        // BP012/BP013's configured severities, read from each app class's own file and kept per app, so one app's setting
        // never applies to another's diagnostics. The compiler applies only global configuration to a generator's
        // diagnostics, not an .editorconfig's [*.cs] section, where a consumer phases a new rule in, so the generator
        // applies that itself.
        var configuredSeverityProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Blueprint.Model.BlueprintAppScanAttribute",
                predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
                transform: static (context, _) => (AppMetadataName: context.TargetSymbol.ToFullMetadataName(), Tree: context.TargetNode.SyntaxTree))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) => (pair.Left.AppMetadataName, Severities: ConfiguredSeverities.Read(pair.Right.GetOptions(pair.Left.Tree))))
            .Collect();

        context.RegisterSourceOutput(
            generationModelProvider.Combine(configuredSeverityProvider),
            (spc, pair) =>
            {
                var (result, configuredSeverities) = pair;
                result!.ReportDiagnostics(spc);

                if (result.Value is not { } classToGenerate)
                {
                    return;
                }

                var severities = configuredSeverities
                    .Where(entry => entry.AppMetadataName == classToGenerate.Class.MetadataName)
                    .Select(static entry => entry.Severities)
                    .FirstOrDefault() ?? ConfiguredSeverities.None;

                foreach (var diagnostic in classToGenerate.Diagnostics)
                {
                    var configured = severities.For(diagnostic.Descriptor.Id);
                    if (configured is null)
                    {
                        spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                    }
                    else if (configured.Value.Report)
                    {
                        spc.ReportDiagnostic(diagnostic.ToDiagnostic(configured.Value.Severity));
                    }
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

        var operationNameRemoval = compilationModel.Class.OperationNameRemoval;

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

                    if (compilationModel.Class.AllowRuntimeTypeFallback)
                    {
                        c.AppendLine("this.AllowRuntimeTypeFallback = true;");
                    }
                }

                foreach (var registration in compilationModel.Operations)
                {
                    c.AppendLine(operationNameRemoval is null
                        ? $"this.Register({registration}.Configure());"
                        : $"this.Register({ApplyOperationNameTransformMethodName}({registration}.Configure()));");
                }

                c.AppendLine(string.Empty);
                c.AppendLine("this.Freeze();");

                // Wire state-based checkers to resource descriptors after Freeze (which derives them)
                if (compilationModel.LinkCheckerTypes.Count > 0)
                {
                    c.AppendLine("PopulateResourceCheckers(this);");
                }
            });

        // The app's [BlueprintAppOperationNames] removal is applied here, to this app's own descriptor instances before
        // they are registered, because the descriptors are generated with the operation — often in a contract assembly
        // that cannot see which app hosts it — and each Configure() call returns a fresh instance. Every later reader
        // of ApiOperationDescriptor.Name in this app then sees the renamed descriptor. RemoveFromOperationName is the
        // compile-time mirror of this code, which the BP011 collision check relies on.
        if (operationNameRemoval is not null)
        {
            classBuilder.AddMethod(ApplyOperationNameTransformMethodName, Accessibility.Private)
                .MakeStaticMethod()
                .WithReturnType("global::Blueprint.Model.ApiOperationDescriptor")
                .AddParameter("global::Blueprint.Model.ApiOperationDescriptor", "descriptor")
                .WithBody(c =>
                {
                    c.AppendLine($"descriptor.Name = descriptor.Name.Replace({CSharpSymbolDisplay.FormatLiteral(operationNameRemoval, quote: true)}, string.Empty, global::System.StringComparison.Ordinal);");
                    c.AppendLine(string.Empty);
                    c.AppendLine("return descriptor;");
                });
        }

        // Always generate RegisterLinkCheckers so consumer call sites compile regardless of
        // whether link checkers currently exist in the scanned assemblies. This also ensures
        // the method is available for codemod-generated call sites that reference it.
        if (compilationModel.LinkCheckerTypes.Count > 0)
        {
            classBuilder.AddMethod("RegisterLinkCheckers", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder", "builder")
                .WithBody(c =>
                {
                    foreach (var checker in compilationModel.LinkCheckerTypes)
                    {
                        c.AppendLine($"builder.AddLinkVisibilityAuthoriser<global::{checker.CheckerTypeName}>();");
                    }
                });

            // Generate PopulateResourceCheckers to wire state-based checkers into ApiResourceDescriptor instances
            classBuilder.AddMethod("PopulateResourceCheckers", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Blueprint.Model.BlueprintApp", "app")
                .WithBody(c =>
                {
                    // Group checkers by resource type to reduce lookups
                    var grouped = compilationModel.LinkCheckerTypes
                        .GroupBy(static x => x.ResourceTypeName)
                        .OrderBy(static g => g.Key, StringComparer.Ordinal);

                    foreach (var resourceGroup in grouped)
                    {
                        c.AppendLine($"if (app.TryGetResourceDescriptor(typeof(global::{resourceGroup.Key}), out var rd_{resourceGroup.Key.Replace(".", "_", StringComparison.Ordinal)}))");
                        c.AppendLine("{");

                        foreach (var checker in resourceGroup)
                        {
                            c.AppendLine($"    rd_{resourceGroup.Key.Replace(".", "_", StringComparison.Ordinal)}.AddStateBasedChecker(typeof(global::{checker.CheckerTypeName}));");
                        }

                        c.AppendLine("}");
                        c.AppendLine(string.Empty);
                    }
                });
        }
        else if (compilationModel.HasServiceBuilder)
        {
            // Emit empty stub so consumer call sites always compile
            classBuilder.AddMethod("RegisterLinkCheckers", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder", "builder")
                .WithBody(_ => { });
        }

        // Generate RegisterApiAuthorisers for IApiAuthoriser auto-discovery
        if (compilationModel.ApiAuthoriserTypes.Count > 0)
        {
            classBuilder.AddMethod("RegisterApiAuthorisers", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder", "builder")
                .WithBody(c =>
                {
                    foreach (var authoriserType in compilationModel.ApiAuthoriserTypes)
                    {
                        c.AppendLine($"builder.AddApiAuthoriser<global::{authoriserType}>();");
                    }
                });
        }
        else if (compilationModel.HasServiceBuilder)
        {
            classBuilder.AddMethod("RegisterApiAuthorisers", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder", "builder")
                .WithBody(_ => { });
        }

        // Generate RegisterResourceExpanders for IResourceExpander auto-discovery
        if (compilationModel.ResourceExpanderTypes.Count > 0)
        {
            classBuilder.AddMethod("RegisterResourceExpanders", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder", "builder")
                .WithBody(c =>
                {
                    foreach (var expanderType in compilationModel.ResourceExpanderTypes)
                    {
                        c.AppendLine($"builder.AddResourceExpander<global::{expanderType}>();");
                    }
                });
        }
        else if (compilationModel.HasServiceBuilder)
        {
            classBuilder.AddMethod("RegisterResourceExpanders", Accessibility.Public)
                .MakeStaticMethod()
                .WithReturnType("void")
                .AddParameter("global::Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder", "builder")
                .WithBody(_ => { });
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


