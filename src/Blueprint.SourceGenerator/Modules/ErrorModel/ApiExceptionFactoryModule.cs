using System.Collections.Immutable;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Gateway;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Scans operation types for static <c>ApiExceptionFactory</c> fields of any accessibility and projects each discovered
/// factory as <see cref="ApiExceptionFactoryRegistration"/> metadata so <see cref="HttpResponseMetadataModule"/> can
/// include the corresponding error status codes in OpenAPI response documentation.
/// </summary>
/// <remarks>
/// The legacy Blueprint convention (<c>ApiExceptionFactoryResponseConvention</c>) performed this discovery at
/// runtime using reflection. This module moves the work to compile time, extracting the HTTP status code from the
/// field initializer constructor arguments (<see cref="ApiExceptionFactoryResolver"/>) and emitting a warning when the
/// initializer cannot be evaluated.
/// </remarks>
public sealed class ApiExceptionFactoryModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private const string ApiExceptionFactoryMetadataName = "Blueprint.ApiExceptionFactory";

    /// <inheritdoc />
    public string ModuleId => nameof(ApiExceptionFactoryModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ApiExceptionFactoryModule);

    /// <inheritdoc />
    public int Order => 65;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        if (HttpHostExposureHelper.HasHttpHostExposure(operationType) == false)
        {
            return;
        }

        var apiExceptionFactoryType = context.FindTypeByMetadataName(ApiExceptionFactoryMetadataName);
        if (apiExceptionFactoryType is null)
        {
            return;
        }

        foreach (var registration in DiscoverFactoryFields(operationType, apiExceptionFactoryType, context))
        {
            context.AddMetadata(registration);
        }

        // A gateway operation documents the inner operation's factories as though it declared them (D14).
        if (OperationPropertyModelBuilder.FindExposedType(operationType, out var exposeAttribute) is { } innerType)
        {
            var location = exposeAttribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? operationType.Locations.FirstOrDefault();

            foreach (var registration in DiscoverInnerFactoryFields(operationType, innerType, apiExceptionFactoryType, context, location))
            {
                context.AddMetadata(registration);
            }
        }
    }

    /// <summary>
    /// Discovers the factories of the inner operation an <c>[Expose&lt;TInner&gt;]</c> operation forwards to, less those
    /// whose problem type one of the operation's <c>[TranslateError]</c>s answers with a factory of its own, which is
    /// documented as the operation's own.
    /// </summary>
    /// <remarks>
    /// A factory's title and type are each documented as a literal where known at compile time: from its initialiser in
    /// this compilation, or from its assembly's <c>[BlueprintApiExceptionStatus]</c> index. Otherwise it is read from the
    /// member at run time, as the operation's own are, when the operation can name it. A factory whose initialiser cannot be
    /// evaluated is left out, as the inner operation's own descriptor reports it.
    /// </remarks>
    private static IEnumerable<ApiExceptionFactoryRegistration> DiscoverInnerFactoryFields(
        INamedTypeSymbol operationType,
        INamedTypeSymbol innerType,
        INamedTypeSymbol apiExceptionFactoryType,
        DescriptorContributorContext context,
        Location? location)
    {
        var translatedTypes = GetTranslatedProblemTypes(operationType);

        foreach (var (named, member) in ApiExceptionFactoryResolver.GetMembers(innerType, apiExceptionFactoryType))
        {
            if (!ApiExceptionFactoryResolver.TryResolve(member, context.GetSemanticModel, out var factory)
                || (factory.Type is { } type && translatedTypes.Contains(type)))
            {
                continue;
            }

            var reference = CanName(named, operationType.ContainingAssembly)
                ? $"{innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{named.Name}"
                : null;

            var title = factory.Title is { } knownTitle ? CSharpSymbolDisplay.FormatLiteral(knownTitle, quote: true) : reference is null ? null : $"{reference}.Title";
            var problemType = factory.Type is { } knownType ? CSharpSymbolDisplay.FormatLiteral(knownType, quote: true) : reference is null ? null : $"{reference}.Type";

            if (title is null || problemType is null)
            {
                continue;
            }

            if (factory.Type is null && translatedTypes.Count > 0)
            {
                context.AddDiagnostic(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.InnerFactoryTypeUnknown,
                    location,
                    new EquatableList<string>([operationType.ToDisplayString(), $"{innerType.ToDisplayString()}.{named.Name}", innerType.ContainingAssembly.Name])));
            }

            yield return new ApiExceptionFactoryRegistration(named.Name, factory.HttpStatus, RenderProblemExample(title, problemType));
        }
    }

    /// <summary>
    /// The upstream problem types an operation's own <c>[TranslateError]</c>s answer.
    /// </summary>
    private static HashSet<string> GetTranslatedProblemTypes(INamedTypeSymbol operationType)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in operationType.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "TranslateErrorAttribute" } attributeClass
                && attributeClass.IsInNamespace("Blueprint", "Model", "Attributes")
                && attribute.ConstructorArguments is [{ Value: string fromType }, _])
            {
                types.Add(fromType);
            }
        }

        return types;
    }

    /// <summary>
    /// Whether code in <paramref name="within"/> can name <paramref name="member"/>: it and every type containing it are
    /// public, or internal to an assembly that gives <paramref name="within"/> access.
    /// </summary>
    private static bool CanName(ISymbol member, IAssemblySymbol within)
    {
        for (var current = member; current is not null and not INamespaceSymbol; current = current.ContainingType)
        {
            var accessible = current.DeclaredAccessibility switch
            {
                Accessibility.Public => true,
                Accessibility.Internal or Accessibility.ProtectedOrInternal => SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, within)
                    || current.ContainingAssembly.GivesAccessTo(within),
                _ => false,
            };

            if (!accessible)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Renders the construction of the <c>OperationProblemExample</c> that documents a factory: from its field's own title
    /// and type, read when the descriptor is built, or from the given title and type expressions.
    /// </summary>
    private static string RenderProblemExample(string fieldReference)
    {
        return RenderProblemExample($"{fieldReference}.Title", $"{fieldReference}.Type");
    }

    private static string RenderProblemExample(string title, string type)
    {
        return $"new global::Blueprint.Model.OperationProblemExample({title}, {type})";
    }

    private static ImmutableArray<ApiExceptionFactoryRegistration> DiscoverFactoryFields(
        INamedTypeSymbol operationType,
        INamedTypeSymbol apiExceptionFactoryType,
        DescriptorContributorContext context)
    {
        var builder = ImmutableArray.CreateBuilder<ApiExceptionFactoryRegistration>();

        // Every static field, whatever its accessibility and whether or not it is readonly: legacy's convention read
        // GetFields(Static | NonPublic | Public) (Signature365 8b40ecc961, ApiExceptionFactoryResponseConvention.cs:16).
        // The generated descriptor is nested in the operation's partial type, so it can read a private field. A static
        // auto-property's compiler-generated backing field is one of those fields, as legacy's GetFields returned it too;
        // the descriptor reads the property it backs, whose initializer is the one the field is given.
        foreach (var (named, member) in ApiExceptionFactoryResolver.GetMembers(operationType, apiExceptionFactoryType))
        {
            if (ApiExceptionFactoryResolver.TryResolve(member, context.GetSemanticModel, out var factory))
            {
                builder.Add(new ApiExceptionFactoryRegistration(
                    named.Name,
                    factory.HttpStatus,
                    RenderProblemExample($"{operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{named.Name}")));
            }
            else
            {
                ReportNonEvaluableInitializer(context, member, named.Name, operationType);
            }
        }

        // Declaration order, as legacy's GetFields returned the fields: it is the order of the example map a status
        // documents (Signature365 8b40ecc961, e.g. Billing.Contract GetInvoiceQuery 404 {MissingStripeAccount,
        // MissingInvoice}). It is deterministic, and HttpResponseMetadataModule groups the responses by status itself.
        return builder.ToImmutable();
    }

    private static void ReportNonEvaluableInitializer(
        DescriptorContributorContext context,
        ISymbol member,
        string memberName,
        INamedTypeSymbol operationType)
    {
        var declaringSyntaxReferences = ApiExceptionFactoryResolver.GetDeclaringSyntaxReferences(member);
        var location = declaringSyntaxReferences.Length > 0
            ? declaringSyntaxReferences[0].GetSyntax().GetLocation()
            : operationType.Locations.FirstOrDefault();

        context.AddDiagnostic(new DiagnosticInfo(
            ApiExceptionFactoryDiagnosticDescriptors.NonEvaluableFactory,
            location,
            new EquatableList<string>([memberName, operationType.ToDisplayString()])));
    }
}
