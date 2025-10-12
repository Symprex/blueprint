namespace Blueprint.SourceGenerator.Helpers.Attributes;

/// <summary>
/// Represents a constructor argument of an injected attribute.
/// </summary>
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

    public TGeneratorType GetValue(ParsedAttribute attribute)
    {
        return (TGeneratorType)attribute.GetConstructorArgument(this).Value!;
    }
}
