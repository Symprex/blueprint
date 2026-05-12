using System.Text;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Generators.ApiResources;

/// <summary>
/// Incremental source generator that fills in the <c>Object</c> property for API resource types.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ApiResourceSourceGenerator : IIncrementalGenerator, ITrackedSourceGenerator
{
    private static readonly DiagnosticDescriptor _missingPartialModifierRule = new(
        id: "BP001",
        title: "Class must be partial",
        messageFormat: "The class '{0}' must be partial to generate the Object property",
        category: "ApiResourceSourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public string[] StageNames { get; } = StageHelpers.FindAll(typeof(Stages));

    /// <summary>
    /// Configures syntax discovery and source emission for partial API resource types.
    /// </summary>
    /// <param name="context">The incremental generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => Transform(ctx))
            .Where(static m => m != null)
            .WithTrackingName(Stages.InitialExtraction);

        context.RegisterSourceOutput(
            classDeclarations,
            static (spc, result) =>
            {
                result!.ReportDiagnostics(spc);

                if (result.Value is not { } classToGenerate)
                {
                    return;
                }

                spc.AddSource(
                    $"{classToGenerate.ClassName}_ObjectProperty.g.cs",
                    SourceText.From(GenerateObjectProperty(classToGenerate), Encoding.UTF8));
            });
    }

    private static Result<ClassToGenerate>? Transform(GeneratorSyntaxContext ctx)
    {
        const string apiResourceSymbol = "Blueprint.Model.ApiResource";
        const string apiResourceCollectionSymbol = "Blueprint.Model.IApiResourceCollection";

        var classNode = (ClassDeclarationSyntax)ctx.Node;
        var model = ctx.SemanticModel.GetDeclaredSymbol(classNode);

        if (model is null || !model.InheritsFrom(apiResourceSymbol) || model.IsAbstract)
        {
            return null;
        }

        // We will not try to implement anything because the class already implements Object
        if (model.HasConcreteProperty("Object"))
        {
            return null;
        }

        var isPartial = classNode.Modifiers.Any(SyntaxKind.PartialKeyword);

        return !isPartial
            ? Result<ClassToGenerate>.Failure(
                    new DiagnosticInfo(_missingPartialModifierRule, classNode.Identifier.GetLocation(), [model.Name]))
            : Result.Success(
            new ClassToGenerate(
                model.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                model.ContainingNamespace.ToDisplayString(),
                model.Name,
                model.ImplementsInterface(apiResourceCollectionSymbol)));
    }

    private static string GenerateObjectProperty(ClassToGenerate classToGenerate)
    {
        var namespaceName = classToGenerate.Namespace;
        var classNameWithGenerics = classToGenerate.FullClassName;

        var objectType = classToGenerate.ClassName
            .Replace("ApiResource", string.Empty)
            .Replace("Resource", string.Empty)
            .Replace("DTO", string.Empty)
            .Camelize();

        if (classToGenerate.IsEnumerable)
        {
            objectType = $"list.{objectType}";
        }

        return $$"""
                 namespace {{namespaceName}}
                 {
                     /// <remarks>
                     /// The object type of this resource is "{{objectType}}".
                     /// </remarks>
                     public partial class {{classNameWithGenerics}}
                     {
                         public override string Object => "{{objectType}}";
                     }
                 }
                 """;
    }

    /// <summary>
    /// Captures the minimal metadata needed to render the generated <c>Object</c> override for an API resource.
    /// </summary>
    /// <param name="FullClassName">The resource type name as it should appear in generated code.</param>
    /// <param name="Namespace">The containing namespace for the generated partial type.</param>
    /// <param name="ClassName">The simple class name used to derive the resource object name.</param>
    /// <param name="IsEnumerable">Indicates whether the resource represents a collection payload.</param>
    private record ClassToGenerate(string FullClassName, string Namespace, string ClassName, bool IsEnumerable);

    /// <summary>
    /// The names of the <see cref="ApiResourceSourceGenerator"/> stages, used in particular to test for the
    /// cacheability of the generator.
    /// </summary>
    private static class Stages
    {
        public const string InitialExtraction = nameof(InitialExtraction);
    }
}
