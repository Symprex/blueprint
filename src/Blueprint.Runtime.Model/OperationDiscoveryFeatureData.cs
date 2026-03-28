namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the approved operation discovery facts.
/// </summary>
/// <param name="Kind">
/// The kind of operation: <c>"command"</c> for <see cref="ICommand"/> implementors, <c>"query"</c> for
/// <see cref="IQuery{T}"/> implementors, or <c>"operation"</c> for bare <see cref="IOperation"/> implementors.
/// </param>
/// <param name="Namespace">The CLR namespace of the operation type.</param>
/// <param name="TypeName">The simple (unqualified) type name of the operation.</param>
public sealed record OperationDiscoveryFeatureData(string Kind, string Namespace, string TypeName);
