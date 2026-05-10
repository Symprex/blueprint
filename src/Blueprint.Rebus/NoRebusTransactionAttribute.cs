namespace Blueprint.Rebus;

/// <summary>
/// Prevents the Rebus transaction scope middleware from wrapping the handler in a transaction,
/// meaning messages published during execution are sent immediately rather than being batched
/// and sent only after successful completion.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoRebusTransactionAttribute : Attribute;
