using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Expands a partial resource key, one not starting with <c>/</c>, into the absolute keys of the same resource within its
/// hierarchy, so that a grant on an ancestor covers it: <c>Folders/fld_1</c> might become
/// <c>/Accounts/acc_1/Folders/fld_1</c>, which a grant of <c>/Accounts/acc_1/*</c> covers. Register one with
/// <c>AddResourceKeyExpander</c>.
/// </summary>
/// <remarks>
/// <para>
/// Expanders are registered scoped, so one may use scoped services such as a database session, and are asked in
/// registration order. The first that answers with anything other than <see langword="null"/> decides the expansion, and
/// later ones are not asked, even when they would have answered differently. An empty answer is an answer: the key is
/// handled, and with no keys to match it is denied.
/// </para>
/// <para>
/// An absolute key is never expanded. A partial key that no expander handles is denied, with a reason naming the key,
/// because a grant can only ever cover an absolute key.
/// </para>
/// </remarks>
public interface IResourceKeyExpander
{
    /// <summary>
    /// Expands <paramref name="partialKey"/>, or declines it.
    /// </summary>
    /// <param name="partialKey">The partial key demanded.</param>
    /// <param name="context">The context of the operation executing, whose scoped services the expander may use.</param>
    /// <param name="cancellationToken">Cancellation token for the expansion.</param>
    /// <returns>
    /// The absolute keys the partial key stands for, any one of which a grant may cover; an empty list when the expander
    /// handles the key but it identifies nothing, such as a resource that does not exist; or <see langword="null"/> when
    /// the key is not this expander's to expand.
    /// </returns>
    ValueTask<IReadOnlyList<ResourceKey>?> ExpandAsync(ResourceKey partialKey, ApiOperationContext context, CancellationToken cancellationToken);
}
