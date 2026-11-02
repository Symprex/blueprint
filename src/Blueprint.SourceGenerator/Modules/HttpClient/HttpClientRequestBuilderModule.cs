using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.LinkGeneration;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.HttpClient;

/// <summary>
/// Source-generates <c>CompiledHttpRequestFeatureData</c> on each operation descriptor so that
/// <c>BlueprintHttpClient</c> can build URLs and query strings without property reflection.
/// <para>
/// Depends on <see cref="LinkGenerationModule"/> for <see cref="UrlRegistration"/> metadata
/// and on <see cref="OperationHttpMethodMetadataModule"/> for HTTP method metadata.
/// </para>
/// </summary>
public sealed class HttpClientRequestBuilderModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    // Route-owned (RoutePlaceholderSyntax, R97/R111): the bound name is whatever is before a `:` (alternate resource
    // property) or `(` (legacy format specifier) — either, or both, or neither. The old regex here only excluded `:`, so
    // a `{Name(format)}` placeholder's whole "Name(format)" was taken as the property name and never matched, leaving the
    // literal placeholder unsubstituted in the built URL.
    private static readonly Regex _placeholderRegex = RoutePlaceholderSyntax.Placeholder;

    /// <inheritdoc />
    public string ModuleId => nameof(HttpClientRequestBuilderModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HttpClientRequestBuilderModule);

    /// <inheritdoc />
    public int Order => 70;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } =
    [
        nameof(LinkGenerationModule),
        nameof(OperationHttpMethodMetadataModule),
        nameof(PropertyOwnershipModule),
    ];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var urlRegistrations = context.GetMetadata<UrlRegistration>().ToList();
        if (urlRegistrations.Count == 0)
        {
            return;
        }

        var operationSymbol = context.GetOperationTypeSymbol();
        var operationFqn = operationSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Collect the first URL template — HttpClient uses the first available URL
        var urlTemplate = urlRegistrations[0].Url;

        // Extract placeholder names from the URL template
        var placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in _placeholderRegex.Matches(urlTemplate))
        {
            placeholders.Add(match.Groups[1].Value);
        }

        // Collect all public instance properties from the operation
        var properties = operationSymbol
            .GetAllMembers()
            .OfType<IPropertySymbol>()
            .Where(static p => p.DeclaredAccessibility == Accessibility.Public
                && !p.IsStatic
                && p.GetMethod is not null
                && !p.GetAttributes().Any(static a => a.AttributeClass?.Name is "ObsoleteAttribute"))
            .ToList();

        // Read property ownership metadata to determine which properties are query params
        // and their correct binding names (camelCase, [FromQuery(Name=...)] overrides, etc.)
        var bindingEntries = context.GetMetadata<PropertyBindingEntriesMetadata>().FirstOrDefault();

        // Build a lookup: CLR property name → query binding name (only for query-source properties).
        // URL placeholder properties are excluded even if the ownership module classified them as
        // Query — the URL builder already handles them as path parameters.
        var queryBindingNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bindingEntries != null)
        {
            foreach (var entry in bindingEntries.Entries)
            {
                if (entry.Source == PropertyOwnershipModule.PropertyOwnershipSource.Query
                    && !placeholders.Contains(entry.PropertyName))
                {
                    queryBindingNames[entry.PropertyName] = entry.BindingName ?? entry.PropertyName;
                }
            }
        }

        // Determine if this operation uses query string parameters: a GET carries its non-URL properties as a query string,
        // every other method (a DELETE included) as its body, as legacy's client sent them (Signature365 8b40ecc961,
        // Common.Core/Api/BlueprintHttpClient.cs:79, :88-91).
        var isQueryMethod = context.GetMetadata<OperationHttpMethodMetadataRegistration>()
            .Any(static m => m.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase));

        var queryProperties = isQueryMethod
            ? GetQueryProperties(placeholders, properties, queryBindingNames)
            : [];

        var builders = new RequestBuilderShape(operationFqn, urlTemplate, placeholders, properties, queryProperties, queryBindingNames);

        if (!CanDeclareHttpClass(operationSymbol))
        {
            context.AddRenderStep(codeWriter => RenderInlineFeatureData(codeWriter, builders, isQueryMethod));
            return;
        }

        context.AddOperationMember(operationClass => RenderHttpClass(operationClass, builders, isQueryMethod));
        context.AddRenderStep(codeWriter => RenderFeatureDataCallingHttpClass(codeWriter, builders));
    }

    /// <summary>
    /// Whether the operation can be given its nested <c>Http</c> class. Not when it already declares a member of that name,
    /// and not when a base type is a concrete operation: that base gets an <c>Http</c> class of its own, which the derived
    /// one would hide (CS0108), and declaring the derived one <c>new</c> fails instead (CS0109) whenever the base has no
    /// route and so no class. Such an operation keeps the inline delegates, so its wire output is unchanged either way.
    /// </summary>
    private static bool CanDeclareHttpClass(INamedTypeSymbol operationSymbol)
    {
        if (!operationSymbol.GetMembers("Http").IsEmpty)
        {
            return false;
        }

        for (var current = operationSymbol.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.GetMembers("Http").IsEmpty
                || (!current.IsAbstract && current.AllInterfaces.Any(static i => i.ToDisplayString() == "Blueprint.Model.IOperation")))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Emits <c>public static class Http</c> on the operation: <c>BuildUrl</c>, and for a GET <c>AppendQueryString</c>, typed
    /// to the operation, so a caller holding the operation's static type builds its request with no descriptor lookup and no
    /// cast. A GET always gets <c>AppendQueryString</c>, which returns the URL unchanged when nothing is query-bound, so that a
    /// caller need not know whether the operation has query properties.
    /// </summary>
    private static void RenderHttpClass(ClassBuilder operationClass, RequestBuilderShape builders, bool isQueryMethod)
    {
        var httpClass = operationClass.AddNestedClass("Http", Accessibility.Public)
            .MakeStaticClass()
            .WithSummary("The typed request builders the Blueprint HTTP client sends this operation with.");

        httpClass.AddMethod("BuildUrl", Accessibility.Public)
            .MakeStaticMethod()
            .WithReturnType("string?")
            .AddParameter(builders.OperationFqn, "operation")
            .WithSummary("Builds the operation's relative URL, with a leading <c>/</c>, or <see langword=\"null\"/> when a route value is null.")
            .WithParameterDoc("operation", "The operation to build the URL of.")
            .WithBody(c =>
            {
                var (nullCheck, urlExpression) = BuildUrlStatements("operation", builders);

                if (nullCheck is not null)
                {
                    c.AppendLine(nullCheck);
                }

                c.AppendLine($"return {urlExpression};");
            });

        if (!isQueryMethod)
        {
            return;
        }

        httpClass.AddMethod("AppendQueryString", Accessibility.Public)
            .MakeStaticMethod()
            .WithReturnType("string")
            .AddParameter(builders.OperationFqn, "operation")
            .AddParameter("string", "url")
            .WithSummary("Appends the operation's query-bound properties to <paramref name=\"url\"/> as a query string.")
            .WithParameterDoc("operation", "The operation whose query-bound properties to append.")
            .WithParameterDoc("url", "The URL to append to, which may already carry a query string.")
            .WithBody(c =>
            {
                if (builders.QueryProperties.Count == 0)
                {
                    c.AppendLine("return url;");
                    return;
                }

                foreach (var statement in BuildQueryStringStatements("operation", builders))
                {
                    c.AppendLine(statement);
                }
            });
    }

    private static void RenderFeatureDataCallingHttpClass(ICodeWriter codeWriter, RequestBuilderShape builders)
    {
        var fqn = builders.OperationFqn;
        var appendQueryString = builders.QueryProperties.Count > 0
            ? $"static (op, url) => {fqn}.Http.AppendQueryString(({fqn})op, url)"
            : "null";

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.CompiledHttpRequestFeatureData("
            + $"static (op) => {fqn}.Http.BuildUrl(({fqn})op), "
            + appendQueryString
            + "));");
    }

    private static void RenderInlineFeatureData(ICodeWriter codeWriter, RequestBuilderShape builders, bool isQueryMethod)
    {
        var (nullCheck, urlExpression) = BuildUrlStatements("typed", builders);
        var urlBuilderLambda = $"static (op) => {{ var typed = ({builders.OperationFqn})op; {(nullCheck is null ? string.Empty : nullCheck + " ")}return {urlExpression}; }}";

        // Build the query string builder lambda for a GET, which carries its non-URL properties
        // as a query string rather than a body
        var queryStringLambda = isQueryMethod && builders.QueryProperties.Count > 0
            ? string.Join(" ", [$"static (op, url) => {{ var typed = ({builders.OperationFqn})op;", .. BuildQueryStringStatements("typed", builders), "}"])
            : null;

        codeWriter.AppendLine(
            $"this.SetFeatureData(new global::Blueprint.Model.CompiledHttpRequestFeatureData("
            + urlBuilderLambda
            + ", "
            + (queryStringLambda ?? "null")
            + "));");
    }

    /// <summary>
    /// The URL builder's body over <paramref name="access"/>, the operation's typed variable: an optional statement
    /// returning <see langword="null"/> when a reference-typed or nullable route value is null, and the URL expression.
    /// </summary>
    private static (string? NullCheck, string UrlExpression) BuildUrlStatements(string access, RequestBuilderShape builders)
    {
        var urlTemplate = builders.UrlTemplate;

        // Build a property lookup: property name (case-insensitive) → property symbol
        var propertyLookup = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in builders.Properties)
        {
            propertyLookup[prop.Name] = prop;
        }

        // Generate null checks for placeholder properties
        var nullChecks = new List<string>();

        foreach (var placeholder in builders.Placeholders)
        {
            if (!propertyLookup.TryGetValue(placeholder, out var prop))
            {
                continue;
            }

            if (!prop.Type.IsValueType || prop.Type.NullableAnnotation == NullableAnnotation.Annotated)
            {
                nullChecks.Add($"{access}.{prop.Name} is null");
            }
        }

        // Build URL using string concatenation with Uri.EscapeDataString
        // We avoid $"" interpolation because global:: is not valid inside interpolation holes in C# < 13
        var segments = new List<string>();
        var lastEnd = 0;

        foreach (Match match in _placeholderRegex.Matches(urlTemplate))
        {
            var name = match.Groups[1].Value;

            // RoutePlaceholderSyntax.Placeholder has no separate "star" group (the `\*{0,2}` is inline, uncaptured), so a
            // catch-all is read off the placeholder's own text instead.
            var isCatchAll = match.Value.Length > 1 && match.Value[1] == '*';

            if (!propertyLookup.TryGetValue(name, out var prop))
            {
                continue;
            }

            // Add literal segment before this placeholder
            if (match.Index > lastEnd)
            {
                segments.Add($"\"{urlTemplate.Substring(lastEnd, match.Index - lastEnd)}\"");
            }

            // Catch-all parameters (e.g. {*FolderPath} or {**FolderPath}) preserve slashes
            // and must not be URL-encoded. Regular parameters are escaped.
            var propertyAccess = $"{access}.{prop.Name}";
            if (isCatchAll)
            {
                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    segments.Add(propertyAccess);
                }
                else if (prop.Type.IsValueType && prop.Type.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    segments.Add($"{propertyAccess}.Value.ToString()!");
                }
                else
                {
                    segments.Add($"{propertyAccess}.ToString()!");
                }
            }
            else if (prop.Type.SpecialType == SpecialType.System_String)
            {
                segments.Add($"global::System.Uri.EscapeDataString({propertyAccess})");
            }
            else if (prop.Type.IsValueType && prop.Type.NullableAnnotation == NullableAnnotation.Annotated)
            {
                segments.Add($"global::System.Uri.EscapeDataString({propertyAccess}.Value.ToString()!)");
            }
            else
            {
                segments.Add($"global::System.Uri.EscapeDataString({propertyAccess}.ToString()!)");
            }

            lastEnd = match.Index + match.Length;
        }

        // Add remaining literal after last placeholder
        if (lastEnd < urlTemplate.Length)
        {
            segments.Add($"\"{urlTemplate[lastEnd..]}\"");
        }

        // Ensure leading slash
        var urlExpr = segments.Count > 0
            ? string.Join(" + ", segments)
            : $"\"{urlTemplate}\"";

        if (!urlTemplate.StartsWith('/'))
        {
            urlExpr = "\"/\" + " + urlExpr;
        }

        var nullCheck = nullChecks.Count > 0
            ? $"if ({string.Join(" || ", nullChecks)}) return null;"
            : null;

        return (nullCheck, urlExpr);
    }

    /// <summary>
    /// The properties a GET carries as its query string. When ownership metadata is available it is the source of truth
    /// for which properties are query params; when it is not (e.g. no HTTP exposure analysis), the previous heuristic
    /// applies: every non-placeholder property.
    /// </summary>
    private static List<IPropertySymbol> GetQueryProperties(
        HashSet<string> placeholders,
        List<IPropertySymbol> properties,
        Dictionary<string, string> queryBindingNames)
    {
        return queryBindingNames.Count > 0
            ? properties.Where(p => queryBindingNames.ContainsKey(p.Name)).ToList()
            : properties.Where(p => !placeholders.Contains(p.Name)).ToList();
    }

    /// <summary>
    /// The query string builder's statements over <paramref name="access"/>, the operation's typed variable, and the
    /// <c>url</c> parameter, ending in a <c>return</c> of the built URL.
    /// </summary>
    private static List<string> BuildQueryStringStatements(string access, RequestBuilderShape builders)
    {
        // Determine whether any property is a collection — collections need List<string>
        // for repeated key expansion (e.g. ?ids=1&ids=2). Scalar-only operations use a
        // zero-allocation StringBuilder approach with separator tracking.
        var hasCollections = builders.QueryProperties.Any(p => IsEnumerableType(p.Type, out _));

        return hasCollections
            ? BuildQueryStringStatementsWithList(access, builders.QueryProperties, builders.QueryBindingNames)
            : BuildQueryStringStatementsScalarOnly(access, builders.QueryProperties, builders.QueryBindingNames);
    }

    /// <summary>
    /// Generates query string statements for operations where ALL query parameters are scalar
    /// (no collections). Uses a zero-allocation separator-tracking pattern instead of List.
    /// </summary>
    private static List<string> BuildQueryStringStatementsScalarOnly(
        string access,
        List<IPropertySymbol> queryProperties,
        Dictionary<string, string> queryBindingNames)
    {
        // Pattern: track separator char, build string with direct concatenation.
        // Generated code shape:
        //   var sep = url.Contains('?') ? '&' : '?';
        //   var qs = url;
        //   qs += sep + "prop=..." ; sep = '&';
        //   return qs;
        var parts = new List<string>
        {
            "var sep = url.Contains('?') ? '&' : '?';",
            "var qs = url;",
        };

        foreach (var prop in queryProperties)
        {
            var propAccess = $"{access}.{prop.Name}";
            var bindingName = ResolveQueryBindingName(prop.Name, queryBindingNames);
            var escapedName = Uri.EscapeDataString(bindingName);

            if (prop.Type.SpecialType == SpecialType.System_Boolean)
            {
                parts.Add($"qs = qs + sep + \"{escapedName}=\" + ({propAccess} ? \"true\" : \"false\"); sep = '&';");
            }
            else if (prop.Type.IsValueType && prop.Type.NullableAnnotation != NullableAnnotation.Annotated)
            {
                parts.Add($"qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}.ToString()!); sep = '&';");
            }
            else
            {
                parts.Add($"if ({propAccess} is not null)");
                parts.Add("{");

                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    parts.Add($"if ({propAccess}.Length > 0) {{ qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}); sep = '&'; }}");
                }
                else if (prop.Type.IsValueType)
                {
                    parts.Add($"qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}.Value.ToString()!); sep = '&';");
                }
                else
                {
                    parts.Add($"{{ var s = {propAccess}.ToString(); if (!string.IsNullOrEmpty(s)) {{ qs = qs + sep + \"{escapedName}=\" + global::System.Uri.EscapeDataString(s); sep = '&'; }} }}");
                }

                parts.Add("}");
            }
        }

        parts.Add("return qs == url ? url : qs;");

        return parts;
    }

    /// <summary>
    /// Generates query string statements for operations that have collection parameters.
    /// Uses List&lt;string&gt; for repeated-key expansion (e.g. ?ids=1&amp;ids=2).
    /// </summary>
    private static List<string> BuildQueryStringStatementsWithList(
        string access,
        List<IPropertySymbol> queryProperties,
        Dictionary<string, string> queryBindingNames)
    {
        var parts = new List<string>
        {
            "var parts = new global::System.Collections.Generic.List<string>();",
        };

        foreach (var prop in queryProperties)
        {
            var propAccess = $"{access}.{prop.Name}";
            var bindingName = ResolveQueryBindingName(prop.Name, queryBindingNames);
            var escapedName = Uri.EscapeDataString(bindingName);

            if (IsEnumerableType(prop.Type, out var elementType))
            {
                EmitCollectionQueryParam(parts, propAccess, escapedName, elementType);
            }
            else if (prop.Type.SpecialType == SpecialType.System_Boolean)
            {
                parts.Add($"parts.Add(\"{escapedName}=\" + ({propAccess} ? \"true\" : \"false\"));");
            }
            else if (prop.Type.IsValueType && prop.Type.NullableAnnotation != NullableAnnotation.Annotated)
            {
                parts.Add($"parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}.ToString()!));");
            }
            else
            {
                parts.Add($"if ({propAccess} is not null)");
                parts.Add("{");

                if (prop.Type.SpecialType == SpecialType.System_String)
                {
                    parts.Add($"if ({propAccess}.Length > 0) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}));");
                }
                else if (prop.Type.IsValueType)
                {
                    parts.Add($"parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString({propAccess}.Value.ToString()!));");
                }
                else
                {
                    parts.Add($"{{ var s = {propAccess}.ToString(); if (!string.IsNullOrEmpty(s)) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(s)); }}");
                }

                parts.Add("}");
            }
        }

        parts.Add("if (parts.Count == 0) return url;");
        parts.Add("var sep = url.Contains('?') ? \"&\" : \"?\";");
        parts.Add("return url + sep + string.Join(\"&\", parts);");

        return parts;
    }

    /// <summary>
    /// Determines whether <paramref name="type"/> is a collection that should be expanded as
    /// repeated query-string keys (e.g. <c>?ids=1&amp;ids=2</c>). Returns the element type
    /// when the type is an array or implements <c>IEnumerable&lt;T&gt;</c> (excluding
    /// <see cref="string"/> which is itself <c>IEnumerable&lt;char&gt;</c>).
    /// </summary>
    private static bool IsEnumerableType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;

        // string is IEnumerable<char> but should not be treated as a collection
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        // Arrays: T[]
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        // IEnumerable<T>, IReadOnlyList<T>, List<T>, IList<T>, etc.
        foreach (var iface in type.AllInterfaces)
        {
            if (iface is INamedTypeSymbol { IsGenericType: true } named
                && named.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                elementType = named.TypeArguments[0];
                return true;
            }
        }

        // Also check if the type itself is IEnumerable<T>
        if (type is INamedTypeSymbol { IsGenericType: true } self
            && self.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            elementType = self.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits a <c>foreach</c> loop that appends one query-string key per element,
    /// producing the standard repeated-key encoding (e.g. <c>?ids=1&amp;ids=2&amp;ids=3</c>).
    /// </summary>
    private static void EmitCollectionQueryParam(
        List<string> parts,
        string propAccess,
        string escapedName,
        ITypeSymbol? elementType)
    {
        parts.Add($"if ({propAccess} is not null)");
        parts.Add("{");
        parts.Add($"foreach (var _item in {propAccess})");
        parts.Add("{");

        if (elementType?.SpecialType == SpecialType.System_String)
        {
            parts.Add($"if (_item is not null && _item.Length > 0) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(_item));");
        }
        else if (elementType?.SpecialType == SpecialType.System_Boolean)
        {
            parts.Add($"parts.Add(\"{escapedName}=\" + (_item ? \"true\" : \"false\"));");
        }
        else if (elementType is { IsValueType: true, NullableAnnotation: not NullableAnnotation.Annotated })
        {
            // Non-nullable value types (int, Guid, etc.) — ToString() is always safe
            parts.Add($"parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(_item.ToString()!));");
        }
        else
        {
            // Reference types or nullable value types — null-check required
            parts.Add($"{{ var s = _item?.ToString(); if (!string.IsNullOrEmpty(s)) parts.Add(\"{escapedName}=\" + global::System.Uri.EscapeDataString(s)); }}");
        }

        parts.Add("}");
        parts.Add("}");
    }

    /// <summary>
    /// Resolves the query string key for a property by looking up the binding name assigned by
    /// <see cref="PropertyOwnershipModule"/>. Falls back to <see cref="GetCamelCaseName"/> when
    /// ownership metadata is not available (defensive — should not happen in normal flow).
    /// </summary>
    private static string ResolveQueryBindingName(string propertyName, Dictionary<string, string> queryBindingNames)
    {
        if (queryBindingNames.TryGetValue(propertyName, out var bindingName))
        {
            return bindingName;
        }

        return GetCamelCaseName(propertyName);
    }

    /// <summary>
    /// Lowercases the first character of a property name, matching the convention used by
    /// <see cref="PropertyOwnershipModule"/> for query string binding names.
    /// </summary>
    private static string GetCamelCaseName(string name)
    {
        return name.Camelize();
    }

    /// <summary>
    /// What the URL and query string builders are generated from, gathered once per operation.
    /// </summary>
    private sealed record RequestBuilderShape(
        string OperationFqn,
        string UrlTemplate,
        HashSet<string> Placeholders,
        List<IPropertySymbol> Properties,
        List<IPropertySymbol> QueryProperties,
        Dictionary<string, string> QueryBindingNames);
}
