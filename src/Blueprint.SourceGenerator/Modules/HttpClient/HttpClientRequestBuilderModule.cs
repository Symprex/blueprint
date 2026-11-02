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
    private const string HttpClassName = "Http";

    // Generated files have no nullable annotation context, so a `string?` return would not compile (CS8669); the attribute
    // gives a nullable-enabled caller the same "may be null" signal.
    private const string MaybeNullReturnAttribute = "return: global::System.Diagnostics.CodeAnalysis.MaybeNull";

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

        // The builders are real static methods on a nested `Http` class of the operation's own partial, so a typed client can
        // call them without a descriptor lookup or a cast. The descriptor's delegates are thin wrappers over them, so both
        // build the same URL. An operation that already declares its own `Http` member keeps the inline delegates only.
        var urlBuilderBody = BuildUrlBuilderBody(urlTemplate, placeholders, properties);

        // A GET carries its non-URL properties as a query string rather than a body. The typed appender exists for every
        // GET; the delegate stays null when no property is query-eligible, as the runtime reads that as "append nothing".
        var queryStringBody = isQueryMethod
            ? BuildQueryStringBody(placeholders, properties, queryBindingNames)
            : null;

        var emitHttpClass = operationSymbol.GetMembers(HttpClassName).IsEmpty;

        if (emitHttpClass)
        {
            context.AddOperationMemberStep(operationClass =>
            {
                // A base operation's generated `Http` is not visible as a symbol, so hiding it is suppressed rather than detected.
                var httpClass = operationClass.AddNestedClass(HttpClassName, Accessibility.Public)
                    .MakeStaticClass()
                    .DisableWarning("CS0108");

                httpClass.AddMethod("BuildUrl")
                    .MakePublicMethod()
                    .MakeStaticMethod()
                    .AddAttribute(MaybeNullReturnAttribute)
                    .WithReturnType("string")
                    .AddParameter(operationFqn, "operation")
                    .WithBody(codeWriter => codeWriter.AppendLine(urlBuilderBody));

                if (isQueryMethod)
                {
                    httpClass.AddMethod("AppendQueryString")
                        .MakePublicMethod()
                        .MakeStaticMethod()
                        .WithReturnType("string")
                        .AddParameter(operationFqn, "operation")
                        .AddParameter("string", "url")
                        .WithBody(codeWriter => codeWriter.AppendLine(queryStringBody ?? "return url;"));
                }
            });
        }

        context.AddRenderStep(codeWriter =>
        {
            RenderCompiledHttpRequestFeatureData(codeWriter, operationFqn, urlBuilderBody, queryStringBody, emitHttpClass);
        });
    }

    private static void RenderCompiledHttpRequestFeatureData(
        ICodeWriter codeWriter,
        string operationFqn,
        string urlBuilderBody,
        string? queryStringBody,
        bool callHttpClass)
    {
        string buildUrl;
        string appendQueryString;

        if (callHttpClass)
        {
            buildUrl = $"static (op) => {operationFqn}.{HttpClassName}.BuildUrl(({operationFqn})op)";
            appendQueryString = queryStringBody is not null
                ? $"static (op, url) => {operationFqn}.{HttpClassName}.AppendQueryString(({operationFqn})op, url)"
                : "null";
        }
        else
        {
            buildUrl = $"static (op) => {{ var operation = ({operationFqn})op; {urlBuilderBody} }}";
            appendQueryString = queryStringBody is not null
                ? $"static (op, url) => {{ var operation = ({operationFqn})op; {queryStringBody} }}"
                : "null";
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.CompiledHttpRequestFeatureData("
            + buildUrl
            + ", "
            + appendQueryString
            + "));");
    }

    private static string BuildUrlBuilderBody(
        string urlTemplate,
        HashSet<string> placeholders,
        List<IPropertySymbol> properties)
    {
        // Build a property lookup: property name (case-insensitive) → property symbol
        var propertyLookup = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in properties)
        {
            propertyLookup[prop.Name] = prop;
        }

        // Generate null checks for placeholder properties
        var nullChecks = new List<string>();

        foreach (var placeholder in placeholders)
        {
            if (!propertyLookup.TryGetValue(placeholder, out var prop))
            {
                continue;
            }

            if (!prop.Type.IsValueType || prop.Type.NullableAnnotation == NullableAnnotation.Annotated)
            {
                nullChecks.Add($"typed.{prop.Name} is null");
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
            var propertyAccess = $"typed.{prop.Name}";
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

        var nullCheckExpr = nullChecks.Count > 0
            ? $"if ({string.Join(" || ", nullChecks)}) return null; "
            : string.Empty;

        return $"var typed = operation; {nullCheckExpr}return {urlExpr};";
    }

    private static string? BuildQueryStringBody(
        HashSet<string> placeholders,
        List<IPropertySymbol> properties,
        Dictionary<string, string> queryBindingNames)
    {
        // When ownership metadata is available, use it as the source of truth for which
        // properties are query params. When not available (e.g. no HTTP exposure analysis),
        // fall back to the previous heuristic: non-placeholder properties on a GET.
        List<IPropertySymbol> queryProperties;
        if (queryBindingNames.Count > 0)
        {
            queryProperties = properties
                .Where(p => queryBindingNames.ContainsKey(p.Name))
                .ToList();
        }
        else
        {
            queryProperties = properties
                .Where(p => !placeholders.Contains(p.Name))
                .ToList();
        }

        if (queryProperties.Count == 0)
        {
            return null;
        }

        // Determine whether any property is a collection — collections need List<string>
        // for repeated key expansion (e.g. ?ids=1&ids=2). Scalar-only operations use a
        // zero-allocation StringBuilder approach with separator tracking.
        var hasCollections = queryProperties.Any(p => IsEnumerableType(p.Type, out _));

        return hasCollections
            ? BuildQueryStringBodyWithList(queryProperties, queryBindingNames)
            : BuildQueryStringBodyScalarOnly(queryProperties, queryBindingNames);
    }

    /// <summary>
    /// Generates a query string builder body for operations where ALL query parameters are scalar
    /// (no collections). Uses a zero-allocation separator-tracking pattern instead of List.
    /// </summary>
    private static string BuildQueryStringBodyScalarOnly(
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
            "var typed = operation;",
            "var sep = url.Contains('?') ? '&' : '?';",
            "var qs = url;",
        };

        foreach (var prop in queryProperties)
        {
            var propAccess = $"typed.{prop.Name}";
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

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Generates a query string builder body for operations that have collection parameters.
    /// Uses List&lt;string&gt; for repeated-key expansion (e.g. ?ids=1&amp;ids=2).
    /// </summary>
    private static string BuildQueryStringBodyWithList(
        List<IPropertySymbol> queryProperties,
        Dictionary<string, string> queryBindingNames)
    {
        var parts = new List<string>
        {
            "var typed = operation;",
            "var parts = new global::System.Collections.Generic.List<string>();",
        };

        foreach (var prop in queryProperties)
        {
            var propAccess = $"typed.{prop.Name}";
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

        return string.Join(" ", parts);
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
}
