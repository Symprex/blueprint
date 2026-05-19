namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks a constructor parameter in a resource-event record as a self-query that the pipeline will execute to resolve
/// the event's resource content. Used when the handler cannot cheaply provide the fully-mapped API resource and instead
/// delegates data retrieval to the generated pipeline via a nested query execution.
/// </summary>
/// <remarks>
/// <para>
/// The marked parameter must be a type that represents a registered query operation. At pipeline execution time, the
/// <c>ResourceEventSelfQueryModule</c> detects the deferred result and executes the query through the generated executor,
/// populating the event's content with the query result before downstream modules (link generation, audit) process it.
/// </para>
/// <para>
/// This attribute is mutually exclusive with <see cref="ResourceEventBodyAttribute"/> on the same parameter. A record
/// may combine <c>[ResourceEventSelfQuery]</c> with <c>[ResourceEventKey]</c> on a separate parameter to provide an
/// upfront resource identity for audit before query resolution completes.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class ResourceEventSelfQueryAttribute : Attribute;
