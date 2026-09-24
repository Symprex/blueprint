using System.Reflection;
using Blueprint.Model.Attributes;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Names the schema component of a CLR type under an <see cref="OpenApiSchemaNaming"/> — the one place a type's
/// component name is derived, so that a component and every reference to it agree.
/// </summary>
internal static class OpenApiSchemaNames
{
    /// <summary>
    /// Returns the schema component name of <paramref name="type"/> under <paramref name="naming"/>.
    /// </summary>
    /// <param name="type">The CLR type.</param>
    /// <param name="naming">The naming in force.</param>
    /// <returns>The component name.</returns>
    public static string Format(Type type, OpenApiSchemaNaming naming)
    {
        ArgumentNullException.ThrowIfNull(type);

        return naming == OpenApiSchemaNaming.Legacy
            ? FormatLegacy(type)
            : FormatDefault(type);
    }

    private static string FormatDefault(Type type)
    {
        if (TryGetDeclaredName(type, out var declaredName))
        {
            return declaredName;
        }

        if (type == typeof(string))
        {
            return "string";
        }

        // Generic types: produce "OuterOfInner" (e.g. ResourceEventOfTemplate) instead of the CLR
        // backtick notation that breaks JSON $ref pointers and is unreadable by TypeScript generators.
        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            var argNames = string.Join("And", args.Select(static a => StripSuffix(a.Name)));
            return $"{GetGenericBaseName(type)}Of{argNames}";
        }

        // Short type name by default — matches what TypeScript generators and most OpenAPI
        // tooling expects (e.g. "TemplateApiResource" not "MyApp_Api_TemplateApiResource").
        return type.Name;
    }

    /// <summary>
    /// NJsonSchema's <c>DefaultSchemaNameGenerator</c>, as legacy Blueprint ran it (NJsonSchema 11.1).
    /// </summary>
    private static string FormatLegacy(Type type)
    {
        if (TryGetDeclaredName(type, out var declaredName))
        {
            return declaredName;
        }

        if (type.IsArray)
        {
            return FormatLegacy(type.GetElementType()!) + "[]";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return "Nullable" + FormatLegacy(underlying);
        }

        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments().Select(FormatLegacy);
            return $"{GetGenericBaseName(type)}Of{string.Join("And", args)}";
        }

        if (type == typeof(short))
        {
            return "Short";
        }

        if (type == typeof(int))
        {
            return "Integer";
        }

        if (type == typeof(long))
        {
            return "Long";
        }

        return type.Name;
    }

    private static bool TryGetDeclaredName(Type type, out string name)
    {
        name = type.GetCustomAttribute<OpenApiSchemaNameAttribute>(inherit: false)?.Name!;

        return name is not null;
    }

    private static string GetGenericBaseName(Type type)
    {
        var baseName = type.Name;
        var backtickIndex = baseName.IndexOf('`');

        return backtickIndex > 0 ? baseName[..backtickIndex] : baseName;
    }

    /// <summary>
    /// Strips common API resource suffixes (ApiResource, Resource, DTO, Response) for concise schema names.
    /// </summary>
    private static string StripSuffix(string name)
    {
        ReadOnlySpan<string> suffixes = ["ApiResource", "Resource", "DTO", "Response"];

        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }
}
