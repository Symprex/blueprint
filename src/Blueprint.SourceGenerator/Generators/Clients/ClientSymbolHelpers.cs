using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Clients;

/// <summary>
/// Symbol and identifier helpers for the generators that emit HTTP client code over operations. The identifier helpers
/// are shared by the typed-client generator (<see cref="BlueprintClientSourceGenerator"/>) and the extension-method
/// generator (<c>TypedHttpClientExtensionsGenerator</c>); <see cref="GetHttpMethod"/> is the extension-method generator's,
/// and <see cref="FindDeclaredResultType"/> the typed-client generator's.
/// </summary>
internal static class ClientSymbolHelpers
{
    /// <summary>
    /// The operation's declared result: the argument of the first <c>IReturn&lt;T&gt;</c> it implements, through its base
    /// classes, as declared.
    /// </summary>
    /// <param name="operationType">The operation type.</param>
    /// <returns>The result type, or <see langword="null"/> when the operation implements no <c>IReturn&lt;T&gt;</c>.</returns>
    internal static ITypeSymbol? FindDeclaredResultType(INamedTypeSymbol operationType)
    {
        foreach (var iface in operationType.AllInterfaces)
        {
            if (iface is { Name: "IReturn", TypeArguments.Length: 1 } && iface.IsInNamespace("Blueprint", "Model"))
            {
                return iface.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    /// The HTTP method the extension-method generator sends an operation with: its first Blueprint HTTP method attribute
    /// in declaration order, or GET for a query and POST for a command. The typed-client generator uses
    /// <c>OperationHttpMethodMetadataModule.ResolveFirstHttpMethod</c> instead, which matches what the descriptor registers.
    /// </summary>
    /// <param name="operationType">The operation type.</param>
    /// <returns>The upper-case method, or <see langword="null"/> for an operation that is neither.</returns>
    internal static string? GetHttpMethod(INamedTypeSymbol operationType)
    {
        // Check for explicit HTTP method attributes (same as OperationHttpMethodMetadataModule)
        foreach (var attr in operationType.GetAttributes())
        {
            if (attr.AttributeClass is not { } attrClass
                || !attrClass.IsInNamespace("Blueprint", "Model", "Attributes"))
            {
                continue;
            }

            switch (attrClass.Name)
            {
                case "HttpGetAttribute":
                    return "GET";
                case "HttpPostAttribute":
                    return "POST";
                case "HttpPutAttribute":
                    return "PUT";
                case "HttpPatchAttribute":
                    return "PATCH";
                case "HttpDeleteAttribute":
                    return "DELETE";
                case "HttpMethodAttribute"
                    when attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string method:
                    return method.ToUpperInvariant();
            }
        }

        // Fall back to interface convention
        foreach (var iface in operationType.AllInterfaces)
        {
            if (iface.IsInNamespace("Blueprint", "Model"))
            {
                if (iface.Name == "IQuery")
                {
                    return "GET";
                }

                if (iface.Name == "ICommand")
                {
                    return "POST";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The parameter name a property is exposed as: its camel-cased name, escaped when that is a reserved keyword.
    /// </summary>
    /// <remarks>
    /// A property named <c>Throw</c> or <c>Event</c> camel-cases to a keyword, which is not a legal parameter name
    /// unescaped. A caller passing it as a named argument escapes it too (<c>@throw: true</c>); an unescaped
    /// <c>throw:</c> does not compile.
    /// </remarks>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The parameter identifier.</returns>
    internal static string ToParameterIdentifier(string propertyName)
    {
        return EscapeIdentifier(ToCamelCase(propertyName));
    }

    /// <summary>
    /// Returns <paramref name="name"/> prefixed with <c>@</c> when it is a reserved C# keyword. A symbol's name is
    /// unescaped, so a property declared <c>@class</c> is named <c>class</c> and must be escaped again when emitted.
    /// Contextual keywords (<c>value</c>, <c>async</c>, <c>var</c>) are legal identifiers here and are left alone.
    /// </summary>
    /// <param name="name">The identifier.</param>
    /// <returns>The identifier, escaped when it is a keyword.</returns>
    internal static string EscapeIdentifier(string name)
    {
        return Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? "@" + name
            : name;
    }

    /// <summary>
    /// Lower-cases the first character of <paramref name="name"/>, or its leading acronym (<c>ID</c> → <c>id</c>,
    /// <c>URLPath</c> → <c>urlPath</c>).
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The camel-cased name.</returns>
    internal static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (char.IsLower(name[0]))
        {
            return name;
        }

        // Handle acronyms (e.g. "ID" → "id", "URL" → "url")
        if (name.Length > 1 && char.IsUpper(name[1]))
        {
            var i = 0;
            while (i < name.Length && char.IsUpper(name[i]))
            {
                i++;
            }

            if (i == name.Length)
            {
                return name.ToLowerInvariant();
            }

            return name[..(i - 1)].ToLowerInvariant() + name[(i - 1)..];
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

