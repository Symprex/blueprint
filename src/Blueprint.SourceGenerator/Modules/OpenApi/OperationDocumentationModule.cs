using System.Text.RegularExpressions;
using System.Xml;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Extracts XML documentation comments from operation types and their properties at compile time,
/// storing them as <c>OperationDocumentationFeatureData</c> so the OpenAPI rendering pipeline can
/// use operation summaries and per-property descriptions without runtime XML file loading or
/// reflection-based <c>[Description]</c> attributes.
/// </summary>
public sealed class OperationDocumentationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private static readonly Regex XmlTagStripper = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <inheritdoc />
    public string ModuleId => nameof(OperationDocumentationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(OperationDocumentationModule);

    /// <inheritdoc />
    public int Order => 66;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var summary = ExtractSummary(operationType);
        var remarks = ExtractRemarks(operationType);
        var propertySummaries = ExtractPropertySummaries(operationType);

        if (summary is null && remarks is null && propertySummaries.Count == 0)
        {
            return;
        }

        context.AddRenderStep(writer => RenderDescriptor(writer, summary, remarks, propertySummaries));
    }

    private static string? ExtractSummary(ISymbol symbol)
    {
        return ExtractXmlSection(symbol, "summary");
    }

    private static string? ExtractRemarks(ISymbol symbol)
    {
        return ExtractXmlSection(symbol, "remarks");
    }

    private static Dictionary<string, string> ExtractPropertySummaries(INamedTypeSymbol operationType)
    {
        var summaries = new Dictionary<string, string>(StringComparer.Ordinal);

        // D10: in declared mode a property with no docs of its own is documented by its inner counterpart's.
        var forwardedType = Gateway.DeclaredForward.FindForwardedType(operationType, out _);

        // Members come most derived first, and as in the property model the first declaration of a name is the property;
        // a base declaration it hides documents nothing.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in Planning.OperationPropertyModelBuilder.GetMembersWithContributed(operationType).OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public || !seen.Add(member.Name))
            {
                continue;
            }

            var propertySummary = ExtractOwnOrOverriddenSummary(member)
                ?? ExtractXmlSection(Gateway.DeclaredForward.GetInheritedDocumentation(operationType, forwardedType, member), "summary");

            if (propertySummary is not null)
            {
                summaries[member.Name] = propertySummary;
            }
        }

        return summaries;
    }

    /// <summary>
    /// The property's own summary, else that of the nearest declaration it overrides.
    /// </summary>
    private static string? ExtractOwnOrOverriddenSummary(IPropertySymbol property)
    {
        for (var current = property; current is not null; current = current.OverriddenProperty)
        {
            if (ExtractSummary(current) is { } summary)
            {
                return summary;
            }
        }

        return null;
    }

    private static string? ExtractXmlSection(ISymbol symbol, string sectionName)
    {
        return ExtractXmlSection(symbol.GetDocumentationCommentXml(), sectionName);
    }

    private static string? ExtractXmlSection(string? xmlComment, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(xmlComment))
        {
            return null;
        }

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlComment);

            var node = doc.SelectSingleNode($"//{sectionName}");

            if (node is null)
            {
                return null;
            }

            // Strip XML tags (like <see cref="..."/>, <paramref/>, etc.) and normalize whitespace
            var rawText = node.InnerXml;
            var stripped = XmlTagStripper.Replace(rawText, string.Empty);
            var normalized = NormalizeWhitespace(stripped);

            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string NormalizeWhitespace(string text)
    {
        var parts = text.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts).Trim();
    }

    private static void RenderDescriptor(
        ICodeWriter codeWriter,
        string? summary,
        string? remarks,
        Dictionary<string, string> propertySummaries)
    {
        var summaryLiteral = summary is not null
            ? CSharpSymbolDisplay.FormatLiteral(summary, quote: true)
            : "null";

        var remarksLiteral = remarks is not null
            ? CSharpSymbolDisplay.FormatLiteral(remarks, quote: true)
            : "null";

        if (propertySummaries.Count == 0)
        {
            codeWriter.AppendLine(
                "this.SetFeatureData(new global::Blueprint.Model.OperationDocumentationFeatureData("
                + $"{summaryLiteral}, {remarksLiteral}, "
                + "global::System.Collections.Immutable.ImmutableDictionary<string, string>.Empty));");
        }
        else
        {
            codeWriter.AppendLine(
                "this.SetFeatureData(new global::Blueprint.Model.OperationDocumentationFeatureData("
                + $"{summaryLiteral}, {remarksLiteral}, "
                + "global::System.Collections.Immutable.ImmutableDictionary.CreateRange(new global::System.Collections.Generic.KeyValuePair<string, string>[]");
            codeWriter.AppendLine("{");

            var entries = propertySummaries.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal).ToList();

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var comma = i < entries.Count - 1 ? "," : string.Empty;
                codeWriter.AppendLine(
                    $"    new({CSharpSymbolDisplay.FormatLiteral(entry.Key, quote: true)}, "
                    + $"{CSharpSymbolDisplay.FormatLiteral(entry.Value, quote: true)}){comma}");
            }

            codeWriter.AppendLine("})));");
        }
    }
}
