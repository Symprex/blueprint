using System.Diagnostics;

namespace Blueprint.SourceGenerator.Helpers.Attributes;

/// <summary>
/// Represents a typed parsed constructor argument for an attribute.
/// </summary>
/// <typeparam name="TType">The type of the parsed constructor argument value.</typeparam>
/// <param name="Definition">The definition of the constructor argument.</param>
/// <param name="Location">The location of the constructor argument.</param>
/// <param name="Value">The value of the constructor argument.</param>
[DebuggerDisplay("{Definition.Name}: {Value} @ {Location}")]
public record ParsedAttributeConstructorArgument<TType>(
    IAttributeConstructorArgumentDefinition Definition,
    LocationInfo Location,
    TType Value);

/// <summary>
/// Represents an untyped parsed constructor argument for an attribute.
/// </summary>
/// <param name="Definition">The definition of the constructor argument.</param>
/// <param name="Location">The location of the constructor argument.</param>
/// <param name="Value">The value of the constructor argument.</param>
[DebuggerDisplay("{Definition.Name}: {Value} @ {Location}")]
public record ParsedAttributeConstructorArgument(
    IAttributeConstructorArgumentDefinition Definition,
    LocationInfo Location,
    object? Value);
