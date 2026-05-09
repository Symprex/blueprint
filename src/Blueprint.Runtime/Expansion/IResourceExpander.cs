using System.Collections;

using Blueprint.Runtime.Exec;

using JetBrains.Annotations;

namespace Blueprint.Runtime.Expansion;

/// <summary>
/// Defines a component that can populate <see cref="Model.Expandable{T}"/> properties on API
/// resources after a successful operation. Implementations are resolved from DI and invoked
/// by the generated expansion pipeline stage when the client requests <c>?expand=PropertyName</c>
/// or when a property is marked <c>[AlwaysExpand]</c>.
/// </summary>
/// <remarks>
/// Register implementations with <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// as <c>IResourceExpander</c>. The generated pipeline discovers <see cref="Model.Expandable{T}"/>
/// properties at compile time and emits typed calls for each expandable property.
/// </remarks>
[PublicAPI]
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IResourceExpander
{
    /// <summary>
    /// Determines whether this expander can populate <see cref="Model.Expandable{T}"/>
    /// instances whose <c>T</c> is <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The resource type to check.</param>
    /// <returns><see langword="true"/> if this expander handles the given type; otherwise <see langword="false"/>.</returns>
    bool CanExpand(Type type);

    /// <summary>
    /// Populates the <see cref="Model.Expandable{T}.Value"/> property for each item in
    /// <paramref name="entities"/>. Implementations may batch-load related resources for
    /// efficiency.
    /// </summary>
    /// <param name="context">The current operation context.</param>
    /// <param name="response">The root response object returned by the operation.</param>
    /// <param name="entities">
    /// The <see cref="Model.Expandable{T}"/> instances to expand. The enumerable contains
    /// only items of the type reported by <see cref="CanExpand"/>.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when expansion is finished.</returns>
    ValueTask ExpandAsync(
        ApiOperationContext context,
        object response,
        IEnumerable entities,
        CancellationToken cancellationToken = default);
}
