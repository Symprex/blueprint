using System.Text;
using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Helpers;
using CodeGenHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.App;

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

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
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
            .Select((scanModelContext, _) => FindOperations(scanModelContext))
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

                spc.AddSource(
                    $"{classToGenerate.Class.Name}_DataModelRegistration.g.cs",
                    SourceText.From(GenerateRegistrations(classToGenerate), Encoding.UTF8));
            });
    }

    private static string GenerateRegistrations(BlueprintAppDataModelCompilationModel compilationModel)
    {
        var codeBuilder = CodeBuilder.Create(compilationModel.Class.Namespace!);
        var classBuilder = codeBuilder.AddClass(compilationModel.Class.Name)
            .WithAccessModifier(Accessibility.Public);

        classBuilder.AddConstructor()
            .WithBody(c =>
            {
                foreach (var registration in compilationModel.Operations)
                {
                    c.AppendLine($"this.Register({registration}.Configure());");
                }
            });

        return codeBuilder.Build();
    }

    private static class Stages
    {
        public const string InitialExtraction = nameof(InitialExtraction);

        public const string CombineWithCompilation = nameof(CombineWithCompilation);

        public const string FindServicesToRegister = nameof(FindServicesToRegister);
    }
}


