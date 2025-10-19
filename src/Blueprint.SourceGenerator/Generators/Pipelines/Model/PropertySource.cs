namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// Represents the source from which a handler class property should be set.
/// Used for base class properties that need to be populated before handler invocation.
/// </summary>
public enum PropertySource
{
    /// <summary>
    /// The property source cannot be determined or is not one of the known types.
    /// </summary>
    Unknown,

    /// <summary>
    /// The property should be set to the ApiOperationContext.
    /// </summary>
    ApiOperationContext,

    /// <summary>
    /// The property should be set to the ILogger instance.
    /// </summary>
    Logger,

    /// <summary>
    /// The property should be set to the CancellationToken from context.OperationCancelled.
    /// </summary>
    CancellationToken,

    /// <summary>
    /// The property should be set to the BlueprintApp from context.App.
    /// </summary>
    BlueprintApp,

    /// <summary>
    /// The property should be set to the user authorization context from context.UserAuthorisationContext.
    /// </summary>
    UserAuthorisationContext,
}
