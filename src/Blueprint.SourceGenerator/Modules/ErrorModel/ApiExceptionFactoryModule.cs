using System.Collections.Immutable;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
/// field initializer constructor arguments and emitting a warning when the initializer cannot be evaluated.
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
        if (OperationPropertyModelBuilder.FindExposedType(operationType, out _) is { } innerType)
        {
            foreach (var registration in DiscoverInnerFactoryFields(operationType, innerType, apiExceptionFactoryType, context))
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
    /// A factory is documented by its title and type where they are known at compile time: from its initialiser in this
    /// compilation, or from its assembly's <c>[BlueprintApiExceptionStatus]</c> index. Otherwise it is read from the field at
    /// run time, as the operation's own are, when the operation can name it. A factory whose initialiser cannot be evaluated
    /// is left out, as the inner operation's own descriptor reports it.
    /// </remarks>
    private static IEnumerable<ApiExceptionFactoryRegistration> DiscoverInnerFactoryFields(
        INamedTypeSymbol operationType,
        INamedTypeSymbol innerType,
        INamedTypeSymbol apiExceptionFactoryType,
        DescriptorContributorContext context)
    {
        var translatedTypes = GetTranslatedProblemTypes(operationType);

        foreach (var member in innerType.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: true } field
                || !SymbolEqualityComparer.Default.Equals(field.Type, apiExceptionFactoryType))
            {
                continue;
            }

            ISymbol named = field.AssociatedSymbol is IPropertySymbol property ? property : field;

            if (!TryExtractFactory(field, context, out var factory)
                || (factory.Type is { } type && translatedTypes.Contains(type)))
            {
                continue;
            }

            string problemExample;
            if (factory is { Title: { } knownTitle, Type: { } knownType })
            {
                problemExample = RenderProblemExample(CSharpSymbolDisplay.FormatLiteral(knownTitle, quote: true), CSharpSymbolDisplay.FormatLiteral(knownType, quote: true));
            }
            else if (CanName(named, operationType.ContainingAssembly))
            {
                problemExample = RenderProblemExample($"{innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{named.Name}");
            }
            else
            {
                continue;
            }

            yield return new ApiExceptionFactoryRegistration(named.Name, factory.HttpStatus, problemExample);
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

        foreach (var member in operationType.GetMembers())
        {
            // Every static field, whatever its accessibility and whether or not it is readonly: legacy's convention read
            // GetFields(Static | NonPublic | Public) (Signature365 8b40ecc961, ApiExceptionFactoryResponseConvention.cs:16).
            // The generated descriptor is nested in the operation's partial type, so it can read a private field.
            if (member is not IFieldSymbol { IsStatic: true } field)
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(field.Type, apiExceptionFactoryType))
            {
                continue;
            }

            // A static auto-property's compiler-generated backing field (<X>k__BackingField) is one of those fields, as
            // legacy's GetFields returned it too. Generated code cannot name it, so the descriptor reads the property it
            // backs, whose initializer is the one the field is given (TryExtractHttpStatusCore).
            var memberName = field.AssociatedSymbol is IPropertySymbol property ? property.Name : field.Name;

            if (TryExtractFactory(field, context, out var factory))
            {
                builder.Add(new ApiExceptionFactoryRegistration(
                    memberName,
                    factory.HttpStatus,
                    RenderProblemExample($"{operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{memberName}")));
            }
            else
            {
                ReportNonEvaluableInitializer(context, field, memberName, operationType);
            }
        }

        // Declaration order, as legacy's GetFields returned the fields: it is the order of the example map a status
        // documents (Signature365 8b40ecc961, e.g. Billing.Contract GetInvoiceQuery 404 {MissingStripeAccount,
        // MissingInvoice}). It is deterministic, and HttpResponseMetadataModule groups the responses by status itself.
        return builder.ToImmutable();
    }

    /// <summary>
    /// Extracts the HTTP status code, and the title and type where they are constants, from the field initializer.
    /// Supports direct <c>new ApiExceptionFactory(...)</c> constructor calls (with <c>int</c> literal or
    /// <c>HttpStatusCode.X</c> enum forms in the third argument) and single-hop field references (e.g.
    /// <c>CommonApiExceptions.NotFound</c>) that themselves have a resolvable constructor initializer.
    /// </summary>
    private static bool TryExtractFactory(
        IFieldSymbol field,
        DescriptorContributorContext context,
        out FactoryDetails factory)
    {
        return TryExtractFactoryCore(field, context, out factory, depth: 0);
    }

    /// <summary>
    /// Core extraction with depth guard to prevent infinite recursion on circular field references.
    /// </summary>
    private static bool TryExtractFactoryCore(
        IFieldSymbol field,
        DescriptorContributorContext context,
        out FactoryDetails factory,
        int depth)
    {
        factory = default;

        // Guard against circular references or deep chains.
        if (depth > 2)
        {
            return false;
        }

        var declaringSyntaxReferences = GetDeclaringSyntaxReferences(field);

        // Cross-assembly fields have no syntax to inspect. Try assembly-attribute index first,
        // then fall back to matching the field name against well-known HttpStatusCode enum members.
        if (declaringSyntaxReferences.Length == 0)
        {
            if (TryResolveFromAssemblyAttributes(field, out factory))
            {
                return true;
            }

            if (WellKnownHttpStatusCodes.TryGetValue(field.Name, out var wellKnownStatus))
            {
                factory = new FactoryDetails(wellKnownStatus, null, null);
                return true;
            }

            return false;
        }

        var syntax = declaringSyntaxReferences[0].GetSyntax();

        // Walk through the syntax to find the initializer expression: a field's, or, for an auto-property's backing field,
        // the property's.
        var initializerExpression = syntax switch
        {
            VariableDeclaratorSyntax { Initializer.Value: var value } => value,
            PropertyDeclarationSyntax { Initializer.Value: var propertyValue } => propertyValue,
            _ => null,
        };

        if (initializerExpression is null)
        {
            return false;
        }

        // Extract constructor arguments from `new ApiExceptionFactory(title, type, httpStatus)`
        // or `new(title, type, httpStatus)`.
        var argumentList = initializerExpression switch
        {
            ObjectCreationExpressionSyntax creation => creation.ArgumentList,
            ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.ArgumentList,
            _ => null,
        };

        if (ApiExceptionFactorySyntax.TryGetArguments(argumentList, out var titleArgument, out var typeArgument, out var statusArgument))
        {
            if (!TryResolveStatusCodeFromExpression(statusArgument!, field, out var httpStatus))
            {
                return false;
            }

            // The title and type are known only when both are constants.
            var constantsModel = context.GetSemanticModel(initializerExpression.SyntaxTree);
            var title = constantsModel?.GetConstantValue(titleArgument!).Value as string;
            var type = constantsModel?.GetConstantValue(typeArgument!).Value as string;

            factory = title is not null && type is not null
                ? new FactoryDetails(httpStatus, title, type)
                : new FactoryDetails(httpStatus, null, null);
            return true;
        }

        // Follow field references using the semantic model: `field = CommonApiExceptions.NotFound`.
        if (initializerExpression is MemberAccessExpressionSyntax or IdentifierNameSyntax)
        {
            var semanticModel = context.GetSemanticModel(initializerExpression.SyntaxTree);
            if (semanticModel is not null)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(initializerExpression);
                if (symbolInfo.Symbol is IFieldSymbol referencedField)
                {
                    return TryExtractFactoryCore(referencedField, context, out factory, depth + 1);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the integer value of the HTTP status code from the constructor argument expression.
    /// </summary>
    private static bool TryResolveStatusCodeFromExpression(ExpressionSyntax expression, IFieldSymbol containingField, out int httpStatus)
    {
        httpStatus = 0;

        switch (expression)
        {
            // Direct int literal: `new ApiExceptionFactory("t", "t", 404)`
            case LiteralExpressionSyntax literal when literal.Token.Value is int intValue:
                httpStatus = intValue;
                return true;

            // Cast expression: `new ApiExceptionFactory("t", "t", (int)HttpStatusCode.NotFound)`
            case CastExpressionSyntax castExpression:
                return TryResolveStatusCodeFromExpression(castExpression.Expression, containingField, out httpStatus);

            // Enum member access: `HttpStatusCode.NotFound`
            case MemberAccessExpressionSyntax memberAccess:
                return TryResolveHttpStatusCodeEnumMember(memberAccess, containingField, out httpStatus);

            default:
                return false;
        }
    }

    /// <summary>
    /// Resolves <c>HttpStatusCode.X</c> to its integer value using the well-known .NET enum mapping.
    /// Falls back to locating the enum field symbol via the containing compilation when the member name
    /// is not in the static map (future-proofing for custom or new status codes).
    /// </summary>
    private static bool TryResolveHttpStatusCodeEnumMember(
        MemberAccessExpressionSyntax memberAccess,
        IFieldSymbol containingField,
        out int httpStatus)
    {
        httpStatus = 0;

        var memberName = memberAccess.Name.Identifier.Text;

        // Fast path: well-known HttpStatusCode members used in practice.
        if (WellKnownHttpStatusCodes.TryGetValue(memberName, out httpStatus))
        {
            return true;
        }

        // Slow path: resolve via the compilation's System.Net.HttpStatusCode enum.
        var compilation = containingField.ContainingType.ContainingModule.ContainingAssembly;
        var httpStatusCodeType = compilation.GetTypeByMetadataName("System.Net.HttpStatusCode");
        if (httpStatusCodeType is null)
        {
            return false;
        }

        foreach (var enumMember in httpStatusCodeType.GetMembers())
        {
            if (enumMember is IFieldSymbol { HasConstantValue: true } enumField
                && string.Equals(enumField.Name, memberName, StringComparison.Ordinal)
                && enumField.ConstantValue is int enumIntValue)
            {
                httpStatus = enumIntValue;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the HTTP status code, and the title and type where indexed, from
    /// <c>[assembly: BlueprintApiExceptionStatus(...)]</c> attributes on the assembly containing the referenced field.
    /// These attributes are emitted by the <c>ApiExceptionStatusIndexGenerator</c> in the referenced assembly's
    /// compilation; one built by an earlier generator indexes the status alone.
    /// </summary>
    private static bool TryResolveFromAssemblyAttributes(IFieldSymbol field, out FactoryDetails factory)
    {
        if (ApiExceptionFactorySyntax.TryReadStatusIndex(field, out var status, out var title, out var type))
        {
            factory = new FactoryDetails(status, title, type);
            return true;
        }

        factory = default;
        return false;
    }

    /// <summary>
    /// Returns where <paramref name="field"/> is declared: its own declarator, or, for an auto-property's compiler-generated
    /// backing field, which has no syntax of its own, the property declaration that carries its initializer.
    /// </summary>
    private static ImmutableArray<SyntaxReference> GetDeclaringSyntaxReferences(IFieldSymbol field)
    {
        return field.AssociatedSymbol is IPropertySymbol property && field.DeclaringSyntaxReferences.Length == 0
            ? property.DeclaringSyntaxReferences
            : field.DeclaringSyntaxReferences;
    }

    private static void ReportNonEvaluableInitializer(
        DescriptorContributorContext context,
        IFieldSymbol field,
        string memberName,
        INamedTypeSymbol operationType)
    {
        var declaringSyntaxReferences = GetDeclaringSyntaxReferences(field);
        var location = declaringSyntaxReferences.Length > 0
            ? declaringSyntaxReferences[0].GetSyntax().GetLocation()
            : operationType.Locations.FirstOrDefault();

        context.AddDiagnostic(new DiagnosticInfo(
            ApiExceptionFactoryDiagnosticDescriptors.NonEvaluableFactory,
            location,
            new EquatableList<string>([memberName, operationType.ToDisplayString()])));
    }

    /// <summary>
    /// Maps commonly used <see cref="System.Net.HttpStatusCode"/> member names to their integer values,
    /// avoiding repeated compilation lookups for the most frequent cases.
    /// </summary>
    private static readonly Dictionary<string, int> WellKnownHttpStatusCodes = new(StringComparer.Ordinal)
    {
        ["OK"] = 200,
        ["Created"] = 201,
        ["Accepted"] = 202,
        ["NoContent"] = 204,
        ["BadRequest"] = 400,
        ["Unauthorized"] = 401,
        ["Forbidden"] = 403,
        ["NotFound"] = 404,
        ["MethodNotAllowed"] = 405,
        ["Conflict"] = 409,
        ["Gone"] = 410,
        ["UnprocessableEntity"] = 422,
        ["TooManyRequests"] = 429,
        ["InternalServerError"] = 500,
        ["NotImplemented"] = 501,
        ["BadGateway"] = 502,
        ["ServiceUnavailable"] = 503,
    };

    /// <summary>
    /// What is known of a factory at compile time: its status, and its title and type when both are constants.
    /// </summary>
    private readonly record struct FactoryDetails(int HttpStatus, string? Title, string? Type);
}
