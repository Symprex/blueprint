namespace Blueprint.Auditing;

/// <summary>
/// Marks an operation class to opt out of automatic Activity/trace creation.
/// When applied, the generated pipeline suppresses the <c>StartActivity</c> call
/// in <see cref="OperationExecutorShell"/>, meaning the operation will not produce
/// a trace span. Logging and audit sink invocation remain unaffected.
/// </summary>
/// <remarks>
/// Use this for noisy or high-frequency operations (e.g. health checks, polling queries)
/// where per-invocation trace spans would create excessive telemetry noise without
/// diagnostic value.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DoNotRecordPerformanceMetricsAttribute : Attribute;
