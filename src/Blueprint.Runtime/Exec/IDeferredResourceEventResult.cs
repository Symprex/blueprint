using Blueprint.Model;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Non-generic interface for <see cref="DeferredResourceEventResult{T}"/> that allows pipeline helpers to interact
/// with deferred resource-event results without knowing the concrete resource type parameter at compile time.
/// </summary>
public interface IDeferredResourceEventResult
{
    /// <summary>
    /// Gets the coarse mutation kind (created, updated, deleted).
    /// </summary>
    ChangeType ChangeType { get; }

    /// <summary>
    /// Gets the stable event identity.
    /// </summary>
    string EventId { get; }

    /// <summary>
    /// Gets the resource identity, if known upfront. Derived from resolved content after
    /// <see cref="SetResolvedContent"/> is called if not specified in the constructor.
    /// </summary>
    string? ResourceId { get; }

    /// <summary>
    /// Gets the self-query operation to execute.
    /// </summary>
    object SelfQuery { get; }

    /// <summary>
    /// Gets the metadata dictionary, if any.
    /// </summary>
    Dictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Gets the secure data dictionary, if any.
    /// </summary>
    Dictionary<string, object>? SecureData { get; }

    /// <summary>
    /// Gets or sets the correlation identifier.
    /// </summary>
    string? CorrelationId { get; set; }

    /// <summary>
    /// Gets the resolved content after <see cref="SetResolvedContent"/> has been called.
    /// Null until the self-query has been executed. Downstream modules (audit, HTTP executor,
    /// link generation) use this to access the resolved resource data.
    /// </summary>
    object? ResolvedContent { get; }

    /// <summary>
    /// Stores the resolved content on the instance after the self-query has been executed.
    /// Also derives <see cref="ResourceId"/> from the content's <c>Id</c> property when not
    /// specified upfront.
    /// </summary>
    /// <param name="content">The resource content resolved by executing the self-query.</param>
    void SetResolvedContent(object content);
}
