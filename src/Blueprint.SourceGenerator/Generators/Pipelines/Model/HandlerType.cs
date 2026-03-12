namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// Represents the type of handler for an operation.
/// </summary>
public enum HandlerType
{
    /// <summary>
    /// The handler is a method directly on the operation class itself.
    /// </summary>
    Inline,

    /// <summary>
    /// The handler is a method on a separate handler class where the first parameter
    /// is the operation type.
    /// </summary>
    External,
}
