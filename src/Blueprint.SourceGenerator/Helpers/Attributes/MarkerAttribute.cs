using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Helpers.Attributes;

/// <summary>
/// Represents an attribute that is to be injected in to the generated code, used for modifying the
/// behaviour of the source generator.
/// </summary>
/// <remarks>
/// <para>
/// This model allows for more strongly typed access to the injected attributes, with source code
/// generation capabilities, to avoid lots of string-based operations.
/// </para>
/// <para>
/// The intended use is to create a single static instance of this class, and then use the
/// <see cref="ParseAll(INamedTypeSymbol)"/> or <see cref="ParseSingle(INamedTypeSymbol)"/>
/// methods to parse the attribute data from Roslyn, in addition to using the <see cref="GenerateSource"/> method to generate
/// the source code for the attribute.
/// </para>
/// </remarks>
public class MarkerAttribute(string fullName, AttributeUsageAttribute? usage = null)
{
    public string Namespace { get; } = fullName[..fullName.LastIndexOf(".", StringComparison.Ordinal)];

    public string Name { get; } = fullName[(fullName.LastIndexOf(".", StringComparison.Ordinal) + 1)..];

    /// <summary>
    /// Documentation for the attribute.
    /// </summary>
    public required string Documentation { get; init; }

    /// <summary>
    /// The list of arguments associated with the attribute.
    /// </summary>
    public List<IAttributeConstructorArgumentDefinition> ConstructorArguments { get; } = new();

    /// <summary>
    /// Finds and parses the first attribute that matches this marker attribute.
    /// </summary>
    /// <param name="typeSymbol">The type on which the attribute is applied.</param>
    /// <returns>A <see cref="ParsedAttribute"/> instance, or <c>null</c> if no attribute matches.</returns>
    public ParsedAttribute? ParseSingle(INamedTypeSymbol typeSymbol)
    {
        var attribs = typeSymbol.GetAttributes();

        var byName = attribs.SingleOrDefault(a => a.AttributeClass?.Name == this.Name);

        if (byName == null)
        {
            return null;
        }

        return this.Parse(typeSymbol, byName);
    }

    /// <summary>
    /// Finds and parses all attributes that matches this marker attribute.
    /// </summary>
    /// <param name="typeSymbol">The type to parse attributes from.</param>
    /// <returns>A <see cref="ParsedAttribute"/> instance, or <c>null</c> if no attribute matches.</returns>
    public IEnumerable<ParsedAttribute> ParseAll(INamedTypeSymbol typeSymbol)
    {
        var attribs = typeSymbol.GetAttributes();

        return attribs
            .Where(a => a.AttributeClass?.Name == this.Name)
            .Select(a => this.Parse(typeSymbol, a))
            .OfType<ParsedAttribute>();
    }

    /// <summary>
    /// Parses a Roslyn <see cref="AttributeData"/> into an <see cref="ParsedAttribute"/> model.
    /// </summary>
    /// <param name="parentType">The type on which the attribute is applied.</param>
    /// <param name="attributeData">The attribute data from Roslyn.</param>
    /// <returns>An <see cref="MarkerAttribute"/> instance or null if parsing fails.</returns>
    private ParsedAttribute? Parse(INamedTypeSymbol parentType, AttributeData attributeData)
    {
        var attributeDataName = attributeData.AttributeClass?.Name;

        if (attributeDataName != this.Name)
        {
            return null;
        }

        if (attributeData.ConstructorArguments.Length != this.ConstructorArguments.Count)
        {
            return null;
        }

        // If this attribute is used in a referenced assembly compared to the "main" assembly the source generator
        // is executing in, then the syntax node will be null.
        var syntaxNode = (AttributeSyntax?)attributeData.ApplicationSyntaxReference?.GetSyntax();
        var constructorArguments = new List<ParsedAttributeConstructorArgument>();

        for (var i = 0; i < this.ConstructorArguments.Count; i++)
        {
            var argDefinition = this.ConstructorArguments[i];
            var argument = attributeData.ConstructorArguments[i];

            var location = syntaxNode?.ArgumentList!.Arguments[i].GetLocation() ?? parentType.Locations.First();
            var locationInfo = LocationInfo.CreateFrom(location);

            constructorArguments.Add(new ParsedAttributeConstructorArgument(argDefinition, locationInfo, argument.Value));
        }

        return new ParsedAttribute(constructorArguments.ToArray());
    }

    /// <summary>
    /// Adds the source code for the attribute to the <see cref="IncrementalGeneratorPostInitializationContext"/>.
    /// </summary>
    /// <param name="initializationContext">The <see cref="IncrementalGeneratorPostInitializationContext"/> to add the source code to.</param>
    public void AddSource(IncrementalGeneratorPostInitializationContext initializationContext)
    {
        initializationContext.AddSource(
            $"{this.Name}_MarkerAttribute.g.cs",
            SourceText.From(this.GenerateSource(), Encoding.UTF8));
    }

    /// <summary>
    /// Generates the source code for the attribute.
    /// </summary>
    /// <returns>The generated source code.</returns>
    public string GenerateSource()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {this.Namespace};");
        sb.AppendLine();

        sb.AppendLine($"/// <summary>\n/// {this.Documentation}\n/// </summary>");

        foreach (var arg in this.ConstructorArguments)
        {
            sb.AppendLine($"/// <param name=\"{arg.Name.ToLower()}\">{arg.Documentation}</param>");
        }

        sb.AppendLine($"[System.AttributeUsage(System.AttributeTargets.{(usage?.ValidOn ?? AttributeTargets.Class).ToString()}, AllowMultiple = {ToBooleanString(usage?.AllowMultiple ?? false)}, Inherited = {ToBooleanString(usage?.Inherited ?? true)})]");
        sb.AppendLine("// MarkerAttribute");
        sb.Append($"internal sealed class {this.Name}(");
        sb.Append(string.Join(", ", this.ConstructorArguments.Select(a => $"{a.Type} {a.Name.ToLower()}")));
        sb.Append(") : global::System.Attribute;");

        return sb.ToString();
    }

    private static string ToBooleanString(bool value)
    {
        return value.ToString().ToLowerInvariant();
    }
}
