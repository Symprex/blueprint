using Blueprint.Runtime.Exec;

namespace Blueprint.Runtime.Middleware;

/// <summary>
/// A runtime extension point that lets consumers auto-populate operation properties from ambient context
/// (e.g., JWT claims, tenant accessor, or correlation state) during the Population pipeline stage.
/// <para>
/// This is the compile-time-safe equivalent of legacy Blueprint's middleware-based property population pattern.
/// In Signature365, <c>SetTenantMiddleware</c> auto-populated <c>TenantId</c> properties from JWT claims;
/// implementors of this interface achieve the same by inspecting the operation instance and claims principal
/// within the generated pipeline's Population stage.
/// </para>
/// <para>
/// Register via <see cref="Microsoft.Extensions.DependencyInjection.BlueprintServiceBuilder.AddPropertyPopulator{T}"/>.
/// Multiple populators are supported and will be called in registration order.
/// </para>
/// </summary>
public interface IPropertyPopulator
{
    /// <summary>
    /// Populates properties on the operation instance from ambient context such as the authenticated
    /// user's claims, tenant accessor state, or correlation identifiers.
    /// <para>
    /// This method runs after HTTP binding (route, body, query) has already set request-sourced values.
    /// Implementations should respect values already present on properties and only fill them when empty
    /// or null, to preserve explicit caller-provided data (allowing the authorization layer to verify
    /// that the caller has access to the specified resource).
    /// </para>
    /// </summary>
    /// <param name="context">The operation context providing access to the operation instance, claims, and services.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous population operation.</returns>
    Task PopulateAsync(ApiOperationContext context, CancellationToken cancellationToken);
}
