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
        title: "Class should be partial for optimized Object property",
        messageFormat: "The class '{0}' should be partial to generate an optimized Object property override; without partial, the base virtual implementation uses reflection at runtime",
        category: "ApiResourceSourceGenerator",
        DiagnosticSeverity.Warning,
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
                    $"{classToGenerate.Namespace}.{classToGenerate.ClassName}_ObjectProperty.g.cs",
                    SourceText.From(GenerateObjectProperty(classToGenerate), Encoding.UTF8));
            });
    }

    private static Result<ClassToGenerate>? Transform(GeneratorSyntaxContext ctx)
    {
        const string apiResourceSymbol = "Blueprint.Model.ApiResource";
        const string apiResourceCollectionSymbol = "Blueprint.Model.IApiResourceCollection";
        const string resourceObjectNameAttribute = "Blueprint.Model.Attributes.ResourceObjectNameAttribute";
        const string jsonSchemaAttribute = "NJsonSchema.Annotations.JsonSchemaAttribute";

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

        if (!isPartial)
        {
            return Result<ClassToGenerate>.Failure(
                new DiagnosticInfo(_missingPartialModifierRule, classNode.Identifier.GetLocation(), [model.Name]));
        }

        // Check for explicit [ResourceObjectName("name")] or [JsonSchema("Name")] overrides
        var explicitName = GetExplicitObjectName(model, resourceObjectNameAttribute, jsonSchemaAttribute);

        return Result.Success(
            new ClassToGenerate(
                model.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                model.ContainingNamespace.ToDisplayString(),
                model.Name,
                model.ImplementsInterface(apiResourceCollectionSymbol),
                explicitName));
    }

    /// <summary>
    /// Checks for <c>[ResourceObjectName]</c> or <c>[JsonSchema]</c> attributes and extracts
    /// the explicit object name if present. <c>[ResourceObjectName]</c> takes priority.
    /// </summary>
    private static string? GetExplicitObjectName(INamedTypeSymbol model, string resourceObjectNameAttr, string jsonSchemaAttr)
    {
        foreach (var attr in model.GetAttributes())
        {
            var attrClass = attr.AttributeClass?.ToDisplayString();

            if (attrClass == resourceObjectNameAttr && attr.ConstructorArguments.Length >= 1)
            {
                var name = attr.ConstructorArguments[0].Value as string;
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
        }

        // Fallback to [JsonSchema("Name")] for backward compatibility
        foreach (var attr in model.GetAttributes())
        {
            var attrClass = attr.AttributeClass?.ToDisplayString();

            if (attrClass == jsonSchemaAttr && attr.ConstructorArguments.Length >= 1)
            {
                var name = attr.ConstructorArguments[0].Value as string;
                if (!string.IsNullOrEmpty(name))
                {
                    return name!.LegacyCamelize();
                }
            }
        }

        return null;
    }

    private static string GenerateObjectProperty(ClassToGenerate classToGenerate)
    {
        var namespaceName = classToGenerate.Namespace;
        var classNameWithGenerics = classToGenerate.FullClassName;

        // Named as ApiResource.GetTypeName names a resource, which is legacy Blueprint's rule, so that a resource's $object
        // and its events' eventId and resourceObject agree.
        var objectType = classToGenerate.ExplicitName ?? classToGenerate.ClassName.ToLegacyResourceTypeName();

        if (classToGenerate.IsEnumerable)
        {
            objectType = $"list.{objectType}";
        }

        return $$"""
                 #nullable enable

                 namespace {{namespaceName}}
                 {
                     /// <remarks>
                     /// The object type of this resource is "{{objectType}}".
                     /// </remarks>
                     public partial class {{classNameWithGenerics}}
                     {
                         /// <inheritdoc />
                         [global::System.Text.Json.Serialization.JsonPropertyName("$object")]
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
    /// <param name="ExplicitName">An explicit object name from <c>[ResourceObjectName]</c> or <c>[JsonSchema]</c>, or null for type-name derivation.</param>
    private record ClassToGenerate(string FullClassName, string Namespace, string ClassName, bool IsEnumerable, string? ExplicitName);

    /// <summary>
    /// The names of the <see cref="ApiResourceSourceGenerator"/> stages, used in particular to test for the
    /// cacheability of the generator.
    /// </summary>
    private static class Stages
    {
        public const string InitialExtraction = nameof(InitialExtraction);
    }
}
