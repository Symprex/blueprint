namespace Blueprint.Persistence;

/// <summary>
/// Marks an operation — a command, query or background task — as exempt from automatic unit-of-work persistence.
/// When applied, the <c>UnitOfWorkModule</c> will not call <c>IUnitOfWork.SaveChangesAsync</c>
/// after the handler executes, even when a unit-of-work service is registered in DI, and the pipeline's Rebus
/// transaction is not enlisted as an outbox.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute on operations that manage their own persistence explicitly, or whose side effects should not be
/// automatically committed (e.g., dry-run or preview operations, or a query whose handler must not keep what it stores).
/// </para>
/// <para>
/// It takes the place of Signature365's own <c>DoNotSaveChangesAttribute</c>, which its legacy
/// <c>SaveMartenChangesMiddleware</c> and <c>UseMartenAsOutboxMiddleware</c> honoured on any operation. Unlike that one,
/// it is read from the operation's own type only, not inherited from a base class.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DoNotSaveChangesAttribute : Attribute;
