using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers.Attributes;

/// <summary>
/// Represents a parsed attribute, one that has been parsed from <see cref="AttributeData" /> based off a <see cref="MarkerAttribute"/>.
/// </summary>
public class ParsedAttribute
{
    private readonly ParsedAttributeConstructorArgument[] _constructorArguments;

    /// <summary>
    /// Initialises a new instance of the <see cref="ParsedAttribute"/> class.
    /// </summary>
    /// <param name="constructorArguments">The constructor arguments for this attribute.</param>
    public ParsedAttribute(ParsedAttributeConstructorArgument[] constructorArguments)
    {
        this._constructorArguments = constructorArguments;
    }

    /// <summary>
    /// Gets the constructor argument for the given <see cref="MarkerAttributeConstructorArgument{TType, TGeneratorType}"/>.
    /// </summary>
    /// <param name="definition">The definition of the constructor argument to get.</param>
    /// <returns>The constructor argument.</returns>
    /// <exception cref="InvalidOperationException">If the constructor argument cannot be found.</exception>
    public ParsedAttributeConstructorArgument<TGeneratorType> GetConstructorArgument<TType, TGeneratorType>(MarkerAttributeConstructorArgument<TType, TGeneratorType> definition)
    {
        var foundArgument = this._constructorArguments.SingleOrDefault(a => a.Definition.Name == definition.Name);

        if (foundArgument == null)
        {
            throw new InvalidOperationException($"Cannot find constructor argument {definition.Name}");
        }

        if (foundArgument.Value is null)
        {
            throw new InvalidOperationException($"Constructor argument {definition.Name} cannot be null");
        }

        if (foundArgument.Value is not TGeneratorType)
        {
            throw new InvalidOperationException($"Constructor argument {definition.Name} is of type {foundArgument.Value?.GetType().Name} but expected {typeof(TGeneratorType).Name}");
        }

        return new ParsedAttributeConstructorArgument<TGeneratorType>(definition, foundArgument.Location, (TGeneratorType)foundArgument.Value!);
    }
}
