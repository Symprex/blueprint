namespace Blueprint.Auditing;

/// <summary>
/// Marks an operation class to opt out of automatic audit recording.
/// When applied, the <c>AuditingModule</c> will not contribute a PostExecution audit step
/// for the decorated operation, regardless of whether an <see cref="IAuditSink"/> is registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DoNotAuditAttribute : Attribute;
