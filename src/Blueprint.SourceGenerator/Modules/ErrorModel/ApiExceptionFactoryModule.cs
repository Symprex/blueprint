using System.Collections.Immutable;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

        var registrations = DiscoverFactoryFields(operationType, apiExceptionFactoryType, context);
        if (registrations.Length == 0)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            context.AddMetadata(registration);
        }
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

            if (TryExtractHttpStatus(field, context, out var httpStatus))
            {
                builder.Add(new ApiExceptionFactoryRegistration(
                    field.Name,
                    httpStatus,
                    $"{operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{field.Name}"));
            }
            else
            {
                ReportNonEvaluableInitializer(context, field, operationType);
            }
        }

        // Deterministic ordering: by status code, then by field name for same status.
        builder.Sort(static (a, b) =>
        {
            var statusComparison = a.HttpStatus.CompareTo(b.HttpStatus);
            return statusComparison != 0
                ? statusComparison
                : string.Compare(a.FieldName, b.FieldName, StringComparison.Ordinal);
        });

        return builder.ToImmutable();
    }

    /// <summary>
    /// Extracts the HTTP status code from the field initializer. Supports direct <c>new ApiExceptionFactory(...)</c>
    /// constructor calls (with <c>int</c> literal or <c>HttpStatusCode.X</c> enum forms in the third argument)
    /// and single-hop field references (e.g. <c>CommonApiExceptions.NotFound</c>) that themselves have a
    /// resolvable constructor initializer.
    /// </summary>
    private static bool TryExtractHttpStatus(
        IFieldSymbol field,
        DescriptorContributorContext context,
        out int httpStatus)
    {
        return TryExtractHttpStatusCore(field, context, out httpStatus, depth: 0);
    }

    /// <summary>
    /// Core extraction with depth guard to prevent infinite recursion on circular field references.
    /// </summary>
    private static bool TryExtractHttpStatusCore(
        IFieldSymbol field,
        DescriptorContributorContext context,
        out int httpStatus,
        int depth)
    {
        httpStatus = 0;

        // Guard against circular references or deep chains.
        if (depth > 2)
        {
            return false;
        }

        // Cross-assembly fields have no syntax to inspect. Try assembly-attribute index first,
        // then fall back to matching the field name against well-known HttpStatusCode enum members.
        if (field.DeclaringSyntaxReferences.Length == 0)
        {
            if (TryResolveFromAssemblyAttributes(field, out httpStatus))
            {
                return true;
            }

            return WellKnownHttpStatusCodes.TryGetValue(field.Name, out httpStatus);
        }

        var syntax = field.DeclaringSyntaxReferences[0].GetSyntax();

        // Walk through the syntax to find the initializer expression.
        var initializerExpression = syntax switch
        {
            VariableDeclaratorSyntax { Initializer.Value: var value } => value,
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

        if (argumentList is { Arguments.Count: >= 3 })
        {
            var statusArgExpression = argumentList.Arguments[2].Expression;
            return TryResolveStatusCodeFromExpression(statusArgExpression, field, out httpStatus);
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
                    return TryExtractHttpStatusCore(referencedField, context, out httpStatus, depth + 1);
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
    /// Resolves the HTTP status code from <c>[assembly: BlueprintApiExceptionStatus(...)]</c> attributes
    /// on the assembly containing the referenced field. These attributes are emitted by the
    /// <c>ApiExceptionStatusIndexGenerator</c> in the referenced assembly's compilation.
    /// </summary>
    private static bool TryResolveFromAssemblyAttributes(IFieldSymbol field, out int httpStatus)
    {
        httpStatus = 0;

        var assembly = field.ContainingAssembly;
        if (assembly is null)
        {
            return false;
        }

        var containingTypeMetadataName = field.ContainingType.ToFullMetadataName();

        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "BlueprintApiExceptionStatusAttribute"
                || attr.ConstructorArguments.Length != 3)
            {
                continue;
            }

            var typeArg = attr.ConstructorArguments[0].Value as string;
            var fieldArg = attr.ConstructorArguments[1].Value as string;

            if (string.Equals(typeArg, containingTypeMetadataName, StringComparison.Ordinal)
                && string.Equals(fieldArg, field.Name, StringComparison.Ordinal)
                && attr.ConstructorArguments[2].Value is int statusCode)
            {
                httpStatus = statusCode;
                return true;
            }
        }

        return false;
    }

    private static void ReportNonEvaluableInitializer(
        DescriptorContributorContext context,
        IFieldSymbol field,
        INamedTypeSymbol operationType)
    {
        var location = field.DeclaringSyntaxReferences.Length > 0
            ? field.DeclaringSyntaxReferences[0].GetSyntax().GetLocation()
            : operationType.Locations.FirstOrDefault();

        context.AddDiagnostic(new DiagnosticInfo(
            ApiExceptionFactoryDiagnosticDescriptors.NonEvaluableFactory,
            location,
            new EquatableList<string>([field.Name, operationType.ToDisplayString()])));
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
}
