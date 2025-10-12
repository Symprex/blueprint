namespace Blueprint.SourceGenerator.Helpers.Attributes;

public interface IAttributeConstructorArgumentDefinition
{
    /// <summary>
    /// The name of the argument.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The type of the argument.
    /// </summary>
    Type Type { get; }

    /// <summary>
    /// Documentation for the argument.
    /// </summary>
    string? Documentation { get; }
}