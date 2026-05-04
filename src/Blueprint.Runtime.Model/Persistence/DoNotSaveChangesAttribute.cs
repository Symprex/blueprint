namespace Blueprint.Persistence;

/// <summary>
/// Marks a command operation as exempt from automatic unit-of-work persistence.
/// When applied, the <c>UnitOfWorkModule</c> will not call <c>IUnitOfWork.SaveChangesAsync</c>
/// after the handler executes, even when a unit-of-work service is registered in DI.
/// </summary>
/// <remarks>
/// Use this attribute on commands that manage their own persistence explicitly, or on commands
/// whose side effects should not be automatically committed (e.g., dry-run or preview operations).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DoNotSaveChangesAttribute : Attribute;
