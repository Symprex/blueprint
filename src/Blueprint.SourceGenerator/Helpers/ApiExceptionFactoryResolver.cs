using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// What is known of an <c>ApiExceptionFactory</c> at compile time: its status, and its title and type each where it is a
/// constant or indexed.
/// </summary>
/// <param name="HttpStatus">The HTTP status.</param>
/// <param name="Title">The problem title, or <see langword="null"/> when it is known only at run time.</param>
/// <param name="Type">The problem type, or <see langword="null"/> when it is known only at run time.</param>
internal readonly record struct ApiExceptionFactoryDetails(int HttpStatus, string? Title, string? Type);

/// <summary>
/// One static <c>ApiExceptionFactory</c> member of a type.
/// </summary>
/// <param name="Named">The member generated code names: the field, or the property an auto-property's backing field backs.</param>
/// <param name="Member">The member whose value is read: the field, its backing field, or a metadata property.</param>
internal readonly record struct ApiExceptionFactoryMember(ISymbol Named, ISymbol Member);

/// <summary>
/// Finds a type's static <c>ApiExceptionFactory</c> members and resolves what each is made with, for
/// <c>ApiExceptionFactoryModule</c>, which documents them, and the descriptor generator's incremental key, which has to
/// change when they do.
/// </summary>
internal static class ApiExceptionFactoryResolver
{
    /// <summary>
    /// Maps commonly used <see cref="System.Net.HttpStatusCode"/> member names to their integer values, avoiding repeated
    /// compilation lookups for the most frequent cases.
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
    /// Every static <c>ApiExceptionFactory</c> field of <paramref name="type"/>, whatever its accessibility, in declaration
    /// order, as legacy's <c>GetFields(Static | NonPublic | Public)</c> read them. A source auto-property is among them as
    /// its backing field. Metadata imports no private field, so a metadata type's static properties are read instead.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="apiExceptionFactoryType">The <c>Blueprint.ApiExceptionFactory</c> type.</param>
    /// <returns>The members.</returns>
    public static IEnumerable<ApiExceptionFactoryMember> GetMembers(INamedTypeSymbol type, INamedTypeSymbol apiExceptionFactoryType)
    {
        var fromMetadata = type.DeclaringSyntaxReferences.IsEmpty;

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: true } field when SymbolEqualityComparer.Default.Equals(field.Type, apiExceptionFactoryType):
                    // Generated code cannot name a backing field (<X>k__BackingField), so it names the property.
                    yield return new ApiExceptionFactoryMember(field.AssociatedSymbol is IPropertySymbol backed ? backed : field, field);
                    break;

                case IPropertySymbol { IsStatic: true } property when fromMetadata && SymbolEqualityComparer.Default.Equals(property.Type, apiExceptionFactoryType):
                    yield return new ApiExceptionFactoryMember(property, property);
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves a factory member: from its initialiser in this compilation, following a reference to another factory
    /// (<c>NotFound = CommonApiExceptions.NotFound</c>), or, for one in metadata, from its assembly's
    /// <c>[BlueprintApiExceptionStatus]</c> index, failing that from a member named after an <c>HttpStatusCode</c>.
    /// </summary>
    /// <param name="member">The field or property.</param>
    /// <param name="getSemanticModel">Gets the semantic model of a tree in this compilation, or <see langword="null"/>.</param>
    /// <param name="factory">What is known of the factory.</param>
    /// <returns>Whether its status is known.</returns>
    public static bool TryResolve(ISymbol member, Func<SyntaxTree, SemanticModel?> getSemanticModel, out ApiExceptionFactoryDetails factory)
    {
        return TryResolve(member, getSemanticModel, out factory, depth: 0);
    }

    private static bool TryResolve(ISymbol member, Func<SyntaxTree, SemanticModel?> getSemanticModel, out ApiExceptionFactoryDetails factory, int depth)
    {
        factory = default;

        // Guard against circular references or deep chains.
        if (depth > 2)
        {
            return false;
        }

        var declaringSyntaxReferences = GetDeclaringSyntaxReferences(member);

        if (declaringSyntaxReferences.Length == 0)
        {
            if (ApiExceptionFactorySyntax.TryReadStatusIndex(member, out var status, out var indexedTitle, out var indexedType))
            {
                factory = new ApiExceptionFactoryDetails(status, indexedTitle, indexedType);
                return true;
            }

            if (WellKnownHttpStatusCodes.TryGetValue(member.Name, out var wellKnownStatus))
            {
                factory = new ApiExceptionFactoryDetails(wellKnownStatus, null, null);
                return true;
            }

            return false;
        }

        // A field's initialiser, or, for an auto-property or its backing field, the property's.
        var initializerExpression = declaringSyntaxReferences[0].GetSyntax() switch
        {
            VariableDeclaratorSyntax { Initializer.Value: var value } => value,
            PropertyDeclarationSyntax { Initializer.Value: var propertyValue } => propertyValue,
            _ => null,
        };

        if (initializerExpression is null)
        {
            return false;
        }

        var semanticModel = getSemanticModel(initializerExpression.SyntaxTree);

        var argumentList = initializerExpression switch
        {
            ObjectCreationExpressionSyntax creation => creation.ArgumentList,
            ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.ArgumentList,
            _ => null,
        };

        if (ApiExceptionFactorySyntax.TryGetArguments(argumentList, out var titleArgument, out var typeArgument, out var statusArgument))
        {
            if (!TryResolveStatusCode(statusArgument!, member, out var httpStatus))
            {
                return false;
            }

            // Each is known where it is a constant, whether or not the other is.
            factory = new ApiExceptionFactoryDetails(
                httpStatus,
                semanticModel?.GetConstantValue(titleArgument!).Value as string,
                semanticModel?.GetConstantValue(typeArgument!).Value as string);
            return true;
        }

        if (initializerExpression is MemberAccessExpressionSyntax or IdentifierNameSyntax
            && semanticModel?.GetSymbolInfo(initializerExpression).Symbol is (IFieldSymbol or IPropertySymbol) and var referenced)
        {
            return TryResolve(referenced, getSemanticModel, out factory, depth + 1);
        }

        return false;
    }

    /// <summary>
    /// Returns where <paramref name="member"/> is declared: its own declaration, or, for an auto-property's
    /// compiler-generated backing field, which has no syntax of its own, the property declaration that carries its
    /// initialiser.
    /// </summary>
    /// <param name="member">The field or property.</param>
    /// <returns>Its declaring syntax references.</returns>
    public static ImmutableArray<SyntaxReference> GetDeclaringSyntaxReferences(ISymbol member)
    {
        return member is IFieldSymbol { AssociatedSymbol: IPropertySymbol property } && member.DeclaringSyntaxReferences.Length == 0
            ? property.DeclaringSyntaxReferences
            : member.DeclaringSyntaxReferences;
    }

    private static bool TryResolveStatusCode(ExpressionSyntax expression, ISymbol member, out int httpStatus)
    {
        httpStatus = 0;

        switch (expression)
        {
            // `new ApiExceptionFactory("t", "t", 404)`
            case LiteralExpressionSyntax literal when literal.Token.Value is int intValue:
                httpStatus = intValue;
                return true;

            // `(int)HttpStatusCode.NotFound`
            case CastExpressionSyntax castExpression:
                return TryResolveStatusCode(castExpression.Expression, member, out httpStatus);

            // `HttpStatusCode.NotFound`
            case MemberAccessExpressionSyntax memberAccess:
                return TryResolveHttpStatusCodeEnumMember(memberAccess.Name.Identifier.Text, member, out httpStatus);

            default:
                return false;
        }
    }

    private static bool TryResolveHttpStatusCodeEnumMember(string memberName, ISymbol member, out int httpStatus)
    {
        if (WellKnownHttpStatusCodes.TryGetValue(memberName, out httpStatus))
        {
            return true;
        }

        // A member not in the map, through the compilation's System.Net.HttpStatusCode.
        var httpStatusCodeType = member.ContainingAssembly.GetTypeByMetadataName("System.Net.HttpStatusCode");

        foreach (var enumMember in httpStatusCodeType?.GetMembers() ?? [])
        {
            if (enumMember is IFieldSymbol { HasConstantValue: true, ConstantValue: int enumIntValue } enumField
                && string.Equals(enumField.Name, memberName, StringComparison.Ordinal))
            {
                httpStatus = enumIntValue;
                return true;
            }
        }

        return false;
    }
}
