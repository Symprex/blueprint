using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Reads what an <c>ApiExceptionFactory</c> is made with, for <c>ApiExceptionStatusIndexGenerator</c> and
/// <c>ApiExceptionFactoryModule</c> alike: the constructor arguments of its initialiser, and the
/// <c>[BlueprintApiExceptionStatus]</c> index of the assembly that declares a metadata field.
/// </summary>
internal static class ApiExceptionFactorySyntax
{
    private const string TitleParameter = "title";
    private const string TypeParameter = "type";
    private const string StatusParameter = "httpStatus";

    // Both constructors, (string title, string type, int httpStatus) and (string title, string type, HttpStatusCode httpStatus),
    // take their parameters in this order.
    private static readonly string[] ParameterOrder = [TitleParameter, TypeParameter, StatusParameter];

    /// <summary>
    /// Finds the title, type and status arguments of <c>new ApiExceptionFactory(...)</c> by the parameter each one binds
    /// to, so <c>new(type: ..., httpStatus: ..., title: ...)</c> is read as written rather than by position.
    /// </summary>
    /// <param name="argumentList">The constructor's argument list.</param>
    /// <param name="title">The title argument.</param>
    /// <param name="type">The problem type argument.</param>
    /// <param name="status">The HTTP status argument.</param>
    /// <returns>Whether all three were found.</returns>
    public static bool TryGetArguments(
        ArgumentListSyntax? argumentList,
        out ExpressionSyntax? title,
        out ExpressionSyntax? type,
        out ExpressionSyntax? status)
    {
        title = null;
        type = null;
        status = null;

        if (argumentList is null)
        {
            return false;
        }

        for (var i = 0; i < argumentList.Arguments.Count; i++)
        {
            var argument = argumentList.Arguments[i];

            // A named argument binds by name; an unnamed one, which C# allows before or in its own position, by position.
            var parameter = argument.NameColon?.Name.Identifier.ValueText ?? (i < ParameterOrder.Length ? ParameterOrder[i] : null);

            switch (parameter)
            {
                case TitleParameter:
                    title = argument.Expression;
                    break;
                case TypeParameter:
                    type = argument.Expression;
                    break;
                case StatusParameter:
                    status = argument.Expression;
                    break;
            }
        }

        return title is not null && type is not null && status is not null;
    }

    /// <summary>
    /// Reads a metadata field's entry in the <c>[assembly: BlueprintApiExceptionStatus(...)]</c> index of the assembly that
    /// declares it. An assembly built by an earlier generator indexes the status alone, so its title and type are
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="status">The indexed HTTP status.</param>
    /// <param name="title">The indexed title, or <see langword="null"/>.</param>
    /// <param name="type">The indexed problem type, or <see langword="null"/>.</param>
    /// <returns>Whether the field is indexed.</returns>
    public static bool TryReadStatusIndex(IFieldSymbol field, out int status, out string? title, out string? type)
    {
        status = 0;
        title = null;
        type = null;

        if (field.ContainingAssembly is not { } assembly)
        {
            return false;
        }

        var containingTypeMetadataName = field.ContainingType.ToFullMetadataName();

        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "BlueprintApiExceptionStatusAttribute"
                || attribute.ConstructorArguments.Length is not (3 or 5))
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is string indexedType
                && string.Equals(indexedType, containingTypeMetadataName, StringComparison.Ordinal)
                && attribute.ConstructorArguments[1].Value is string indexedField
                && string.Equals(indexedField, field.Name, StringComparison.Ordinal)
                && attribute.ConstructorArguments[2].Value is int indexedStatus)
            {
                status = indexedStatus;

                if (attribute.ConstructorArguments is [_, _, _, { Value: string indexedTitle }, { Value: string indexedProblemType }])
                {
                    title = indexedTitle;
                    type = indexedProblemType;
                }

                return true;
            }
        }

        return false;
    }
}
