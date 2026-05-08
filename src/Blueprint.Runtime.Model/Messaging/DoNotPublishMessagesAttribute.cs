using JetBrains.Annotations;

namespace Blueprint.Messaging;

/// <summary>
/// Suppresses the generated message-outbox commit for a single command operation.
/// When applied, the pipeline will not call <see cref="IMessageOutbox.CommitAsync"/>
/// after handler execution, even if an outbox implementation is registered.
/// </summary>
/// <remarks>
/// This is useful for commands that intentionally do not produce messages, or for
/// operations where message publication must be deferred to a later stage controlled
/// by the handler itself.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DoNotPublishMessagesAttribute : Attribute;
