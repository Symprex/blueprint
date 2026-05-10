using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter;
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
    private static readonly Regex _placeholderRegex = new(@"\{(?<star>\*)?(?<name>[^:}]+)(?::(?<constraint>[^}]+))?\}", RegexOptions.Compiled);

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
            placeholders.Add(match.Groups["name"].Value);
        }

        // Collect all public instance properties from the operation
        var properties = operationSymbol
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(static p => p.DeclaredAccessibility == Accessibility.Public
                && !p.IsStatic
                && p.GetMethod is not null
                && !p.GetAttributes().Any(static a => a.AttributeClass?.Name is "ObsoleteAttribute"))
            .ToList();

        // Determine if this is a GET request (needs query string builder)
        var isGet = context.GetMetadata<OperationHttpMethodMetadataRegistration>()
            .Any(static m => m.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase));

        context.AddRenderStep(codeWriter =>
        {
            RenderCompiledHttpRequestFeatureData(codeWriter, operationFqn, urlTemplate, placeholders, properties, isGet);
        });
    }

    private static void RenderCompiledHttpRequestFeatureData(
        ICodeWriter codeWriter,
        string operationFqn,
        string urlTemplate,
        HashSet<string> placeholders,
        List<IPropertySymbol> properties,
        bool isGet)
    {
        // Build the URL builder lambda
        var urlBuilderLambda = BuildUrlBuilderLambda(operationFqn, urlTemplate, placeholders, properties);

        // Build the query string builder lambda (only for GET operations with non-URL properties)
        var queryStringLambda = isGet
            ? BuildQueryStringLambda(operationFqn, placeholders, properties)
            : null;

        codeWriter.AppendLine(
            $"this.SetFeatureData(new global::Blueprint.Model.CompiledHttpRequestFeatureData("
            + urlBuilderLambda
            + ", "
            + (queryStringLambda ?? "null")
            + "));");
    }

    private static string BuildUrlBuilderLambda(
        string operationFqn,
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
            var name = match.Groups["name"].Value;
            if (!propertyLookup.TryGetValue(name, out var prop))
            {
                continue;
            }

            // Add literal segment before this placeholder
            if (match.Index > lastEnd)
            {
                segments.Add($"\"{urlTemplate.Substring(lastEnd, match.Index - lastEnd)}\"");
            }

            // Add the escaped property value
            var propertyAccess = $"typed.{prop.Name}";
            if (prop.Type.SpecialType == SpecialType.System_String)
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

        return $"static (op) => {{ var typed = ({operationFqn})op; {nullCheckExpr}return {urlExpr}; }}";
    }

    private static string? BuildQueryStringLambda(
        string operationFqn,
        HashSet<string> placeholders,
        List<IPropertySymbol> properties)
    {
        // Properties eligible for query string: public, not in URL placeholders
        var queryProperties = properties
            .Where(p => !placeholders.Contains(p.Name))
            .ToList();

        if (queryProperties.Count == 0)
        {
            return null;
        }

        // Build a static lambda that appends query string params using string concat (not interpolation)
        // to avoid global:: inside interpolation holes which requires C# 13+
        var parts = new List<string>
        {
            $"static (op, url) => {{ var typed = ({operationFqn})op;",
            "var parts = new global::System.Collections.Generic.List<string>();",
        };

        foreach (var prop in queryProperties)
        {
            var propAccess = $"typed.{prop.Name}";
            var escapedName = Uri.EscapeDataString(prop.Name);

            if (prop.Type.SpecialType == SpecialType.System_Boolean)
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
        parts.Add("return url + sep + string.Join(\"&\", parts); }");

        return string.Join(" ", parts);
    }
}
