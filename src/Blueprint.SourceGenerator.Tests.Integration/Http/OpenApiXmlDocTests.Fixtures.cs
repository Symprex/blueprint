using System.ComponentModel;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

// The documented types OpenApiXmlDocTests describes. Their documentation comments are the input legacy's NJsonSchema and
// legacy's XmlDocsExtensions were run on to take each expected description, so they must not be reworded, re-wrapped or
// moved to another namespace without the expectations being taken again.

/// <summary>
/// A type whose summary
/// spans two lines.
/// </summary>
/// <remarks>Type remarks.</remarks>
public class XmlDocShape : XmlDocBase, IXmlDocShape
{
    /// <summary>A single line.</summary>
    public string Single { get; set; } = string.Empty;

    /// <summary>
    /// First line.
    /// Second line.
    ///
    ///     Indented line.
    /// </summary>
    public string MultiLine { get; set; } = string.Empty;

    /// <summary>Refers to <see cref="XmlDocChild"/>, <see cref="List{T}"/>, <see cref="XmlDocShape.Single"/>, <see langword="null"/>, <see href="https://example.test"/> and <see cref="XmlDocChild">a child</see>.</summary>
    public string Crefs { get; set; } = string.Empty;

    /// <summary>Uses <c>code</c> and a <para>paragraph</para>.</summary>
    public string Elements { get; set; } = string.Empty;

    /// <summary>Tom &amp; Jerry &lt;3 "quoted" 'single'.</summary>
    public string Escapes { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override string FromBase { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string FromInterface { get; set; } = string.Empty;

    /// <summary>The summary.</summary>
    [Description("The attribute.")]
    public string BothDescribed { get; set; } = string.Empty;

    /// <summary>The summary.</summary>
    /// <remarks>The remarks.</remarks>
    public string WithRemarks { get; set; } = string.Empty;

    public string Undocumented { get; set; } = string.Empty;

    public XmlDocColour Colour { get; set; }

    /// <summary>A documented colour.</summary>
    public XmlDocColour DocumentedColour { get; set; }

    /// <summary>A documented child reference.</summary>
    public XmlDocChild DocumentedChild { get; set; } = new();

    /// <summary>A generic.</summary>
    public XmlDocGeneric<XmlDocChild> Generic { get; set; } = new();

    public XmlDocDescribedType Described { get; set; } = new();

    public XmlDocDerived Derived { get; set; } = new();
}

/// <summary>A base type.</summary>
public class XmlDocBase
{
    /// <summary>Base summary.</summary>
    public virtual string FromBase { get; set; } = string.Empty;

    /// <summary>Declared on the base only.</summary>
    public string BaseOnly { get; set; } = string.Empty;
}

/// <summary>An interface.</summary>
public interface IXmlDocShape
{
    /// <summary>Interface summary.</summary>
    string FromInterface { get; set; }
}

/// <summary>A colour.</summary>
public enum XmlDocColour
{
    /// <summary>Red.</summary>
    Red,
    Blue,
}

/// <summary>A child.</summary>
public class XmlDocChild
{
    /// <summary>The name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>A generic of <typeparamref name="T"/>, see <see cref="XmlDocGeneric{T}"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
public class XmlDocGeneric<T>
{
    /// <summary>The value.</summary>
    public T? Value { get; set; }
}

/// <summary>Described by summary.</summary>
[Description("Type attribute.")]
public class XmlDocDescribedType
{
    public string X { get; set; } = string.Empty;
}

/// <inheritdoc/>
public class XmlDocDerived : XmlDocBase
{
    /// <summary>Own property.</summary>
    public string Own { get; set; } = string.Empty;
}

/// <summary>A query base.</summary>
public abstract class XmlDocQueryBase
{
    /// <summary>The page, declared on the base.</summary>
    public int Page { get; set; }
}

/// <summary>A note colour, which only a request body refers to.</summary>
public enum XmlDocNoteColour
{
    Red,
    Blue,
}
