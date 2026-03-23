namespace Blueprint.SourceGenerator.Helpers.Attributes;

/// <summary>
/// Represents a constructor argument definition for a generated marker attribute.
/// </summary>
/// <typeparam name="TType">The CLR type exposed by the generated constructor parameter.</typeparam>
/// <typeparam name="TGeneratorType">The strongly typed value shape used by generator-side consumers.</typeparam>
public class MarkerAttributeConstructorArgument<TType, TGeneratorType>(
    string name,
    string? documentation = null) : IAttributeConstructorArgumentDefinition
{
    /// <summary>
    /// The name of the argument.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// The type of the argument.
    /// </summary>
    public Type Type { get; } = typeof(TType);

    /// <summary>
    /// The default value of the argument.
    /// </summary>
    public bool IsRequired { get; } = Nullable.GetUnderlyingType(typeof(TType)) != null;

    /// <summary>
    /// Documentation for the argument.
    /// </summary>
    public string? Documentation { get; } = documentation;

    /// <summary>
    /// Reads and casts the parsed constructor argument value from a marker attribute instance.
    /// </summary>
    /// <param name="attribute">The parsed marker attribute.</param>
    /// <returns>The constructor argument value in the generator-facing shape.</returns>
    public TGeneratorType GetValue(ParsedAttribute attribute)
    {
        return (TGeneratorType)attribute.GetConstructorArgument(this).Value!;
    }
}
