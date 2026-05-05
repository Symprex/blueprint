namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks an operation as allowing anonymous (unauthenticated) access.
/// When applied, the generated pipeline will not enforce any identity checks,
/// and the descriptor will carry <see cref="AuthenticationRequirement.Anonymous"/> metadata.
/// Cannot be combined with <see cref="MustBeAuthenticatedAttribute"/> on the same operation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowAnonymousAttribute : Attribute;
