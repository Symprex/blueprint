using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Generators.Clients;

/// <summary>
/// What a generated caller of a remote Blueprint API reads off an operation's type, shared by the generators that emit such
/// calls: <see cref="TypedClientGenerator"/> and the app's typed-client extension methods
/// (<c>TypedHttpClientExtensionsGenerator</c>), so that both read an operation's result and write its names the same way.
/// </summary>
internal static class OperationClientShape
{
    /// <summary>
    /// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> that also renders nullable reference type annotations
    /// (<c>string?</c>), generic type arguments included, so that a generated signature states the declared type exactly
    /// and a caller sees no <c>Dictionary&lt;string, string&gt;</c> against <c>Dictionary&lt;string, string?&gt;</c>
    /// nullability mismatch.
    /// </summary>
    public static readonly SymbolDisplayFormat FullyQualifiedWithNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Finds the result an operation declares: the type argument of the first <c>IReturn&lt;T&gt;</c> among all of its
    /// interfaces, those it inherits from a base class included, which is what <c>ICommand&lt;T&gt;</c> and
    /// <c>IQuery&lt;T&gt;</c> declare it through.
    /// </summary>
    /// <param name="operationType">The operation's type.</param>
    /// <returns>The declared result type, or <see langword="null"/> when the operation declares none.</returns>
    public static ITypeSymbol? FindDeclaredResultType(INamedTypeSymbol operationType)
    {
        foreach (var candidate in operationType.AllInterfaces)
        {
            if (candidate is { Name: "IReturn", Arity: 1 } && candidate.IsInNamespace("Blueprint", "Model"))
            {
                return candidate.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the resource a resource event type carries: <c>T</c> of the <c>Blueprint.Http.ResourceEvent&lt;T&gt;</c> that
    /// <paramref name="type"/> is or derives from, which covers <c>ResourceCreated&lt;T&gt;</c>,
    /// <c>ResourceUpdated&lt;T&gt;</c> and <c>ResourceDeleted&lt;T&gt;</c>.
    /// </summary>
    /// <param name="type">The type to test, typically an operation's declared result.</param>
    /// <returns>The resource type, or <see langword="null"/> when <paramref name="type"/> is no resource event of one.</returns>
    public static ITypeSymbol? FindResourceEventResourceType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current is { IsGenericType: true, Name: "ResourceEvent" }
                && current.TypeArguments.Length == 1
                && current.IsInNamespace("Blueprint", "Http"))
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Returns <paramref name="name"/> prefixed with <c>@</c> when it is a reserved C# keyword. A symbol's name is
    /// unescaped, so a property declared <c>@class</c> is named <c>class</c> and must be escaped again when emitted.
    /// Contextual keywords (<c>value</c>, <c>async</c>, <c>var</c>) are legal identifiers here and are left alone.
    /// </summary>
    /// <param name="name">The identifier, as a symbol names it.</param>
    /// <returns>The identifier as it can be written in generated code.</returns>
    public static string EscapeIdentifier(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;
    }
}
