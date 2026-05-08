using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates a business-level <see cref="IApiAuthoriser"/> that enforces tenant isolation.
/// If the authenticated user's account does not match the operation's <c>TenantId</c> property,
/// the request is denied with a 403 Forbidden response.
/// </summary>
/// <remarks>
/// <para>
/// This is the consumer-space equivalent of Signature365's tenant-scoped authorisation checks.
/// The generated pipeline calls <see cref="IApiAuthoriser.CanExecuteOperationAsync"/> during the
/// Authorisation stage, after authentication and user-context loading have completed.
/// </para>
/// <para>
/// To register: <c>blueprint.AddApiAuthoriser&lt;TenantIsolationApiAuthoriser&gt;()</c>
/// </para>
/// </remarks>
public sealed class TenantIsolationApiAuthoriser(ILogger<TenantIsolationApiAuthoriser> logger) : IApiAuthoriser
{
    /// <inheritdoc />
    public bool AppliesTo(ApiOperationDescriptor descriptor)
    {
        // Only applies to operations that carry a TenantId property
        return descriptor.OperationType.GetProperty("TenantId") is not null;
    }

    /// <inheritdoc />
    public Task<ExecutionAllowed> CanExecuteOperationAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object operation)
    {
        var userContext = operationContext.UserAuthorisationContext;

        if (userContext is null)
        {
            logger.LogWarning(
                "Tenant isolation check skipped — no user context available for {OperationType}",
                descriptor.OperationType.Name);

            return ExecutionAllowed.YesTask;
        }

        var tenantIdProp = operation.GetType().GetProperty("TenantId");
        var tenantId = tenantIdProp?.GetValue(operation)?.ToString();

        if (tenantId is null)
        {
            // No tenant ID set on the operation — allow (property populator may not have run yet)
            return ExecutionAllowed.YesTask;
        }

        if (string.Equals(userContext.AccountId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionAllowed.YesTask;
        }

        logger.LogWarning(
            "Tenant isolation denied: user {UserId} (account {AccountId}) attempted to access tenant {TenantId}",
            userContext.Id,
            userContext.AccountId,
            tenantId);

        return Task.FromResult(
            ExecutionAllowed.No(
                $"User does not have access to tenant '{tenantId}'",
                "tenant-isolation",
                ExecutionAllowedFailureType.Authorisation));
    }

    /// <inheritdoc />
    public Task<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object resource)
    {
        return ExecutionAllowed.YesTask;
    }
}
