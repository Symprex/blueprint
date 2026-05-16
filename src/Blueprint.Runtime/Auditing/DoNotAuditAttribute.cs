namespace Blueprint.Auditing;

/// <summary>
/// Marks an operation class or property to opt out of automatic audit recording.
/// When applied to a class, the <c>AuditingModule</c> will not contribute a PostExecution audit step
/// for the decorated operation, regardless of whether an <see cref="IAuditSink"/> is registered.
/// When applied to a property, the audit sink should exclude that property's value from the audit record
/// (e.g. to protect sensitive data such as credentials or PII).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class DoNotAuditAttribute : Attribute;
