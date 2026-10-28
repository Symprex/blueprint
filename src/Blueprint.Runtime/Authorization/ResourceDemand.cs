using Blueprint.Model;

namespace Blueprint.Authorization;

/// <summary>
/// A demand for access to one resource: the operation being authorised and the key of the resource it would act on. An
/// <see cref="IResourceAccessChecker"/> decides it against the grants the caller holds for that operation.
/// </summary>
/// <remarks>
/// <para>
/// The key may be partial, that is not starting with <c>/</c>, such as <c>Folders/fld_1</c>. The checker expands a
/// partial key through the registered <see cref="IResourceKeyExpander"/>s before matching it, because a grant only ever
/// covers an absolute key.
/// </para>
/// <para>
/// <c>default(ResourceDemand)</c> names no operation, so a checker refuses it as an argument error rather than deciding it.
/// </para>
/// </remarks>
/// <param name="Operation">
/// The operation being authorised. For a link it is the linked operation, not the one whose response carries the link.
/// </param>
/// <param name="ResourceKey">The key of the resource demanded, absolute or partial.</param>
public readonly record struct ResourceDemand(ApiOperationDescriptor Operation, ResourceKey ResourceKey);
