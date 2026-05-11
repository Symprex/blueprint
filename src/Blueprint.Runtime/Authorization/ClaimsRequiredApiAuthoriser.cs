using System.Collections.Concurrent;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// An <see cref="IApiAuthoriser"/> that enforces claims-based authorization by evaluating
/// <see cref="ClaimRequiredAttribute"/> instances declared on operation types.
/// </summary>
/// <remarks>
/// <para>
/// This authoriser applies to any operation whose type carries one or more
/// <see cref="ClaimRequiredAttribute"/>. At runtime it resolves the demanded claim
/// from the attribute (substituting property tokens), then checks whether the user's
/// <see cref="IClaimsHolder"/> satisfies the demand via <see cref="IClaimInspector"/>.
/// </para>
/// <para>
/// Register this authoriser in DI via
/// <c>services.AddSingleton&lt;IApiAuthoriser, ClaimsRequiredApiAuthoriser&gt;()</c>
/// or via the convenience method on <c>ServiceCollectionExtensions</c>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ClaimsRequiredApiAuthoriser : IApiAuthoriser
{
    private readonly IClaimInspector _claimInspector;
    private readonly ConcurrentDictionary<Type, ClaimRequiredAttribute[]> _claimAttributeCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimsRequiredApiAuthoriser"/> class.
    /// </summary>
    /// <param name="claimInspector">The claim inspector used to evaluate demands against user claims.</param>
    public ClaimsRequiredApiAuthoriser(IClaimInspector claimInspector)
    {
        this._claimInspector = claimInspector;
    }

    /// <inheritdoc />
    public bool AppliesTo(ApiOperationDescriptor descriptor)
    {
        return this.GetClaimAttributes(descriptor).Length > 0;
    }

    /// <inheritdoc />
    public Task<ExecutionAllowed> CanExecuteOperationAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object operation)
    {
        return this.EvaluateClaims(operationContext, descriptor, operation);
    }

    /// <inheritdoc />
    public Task<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object resource)
    {
        return this.EvaluateClaims(operationContext, descriptor, resource);
    }

    private Task<ExecutionAllowed> EvaluateClaims(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object resource)
    {
        var userContext = operationContext.UserAuthorisationContext;

        if (userContext == null || userContext.IsAnonymous)
        {
            return Task.FromResult(ExecutionAllowed.No(
                "unauthenticated",
                "Authentication is required to access this resource.",
                ExecutionAllowedFailureType.Authentication));
        }

        if (!userContext.IsActive)
        {
            return Task.FromResult(ExecutionAllowed.No(
                "inactive_account",
                "Your account is not active.",
                ExecutionAllowedFailureType.Authorisation));
        }

        // Get the IClaimsHolder from the user context
        if (userContext is not IClaimsHolder claimsHolder)
        {
            return Task.FromResult(ExecutionAllowed.No(
                "claims_not_available",
                "User context does not support claims-based authorization.",
                ExecutionAllowedFailureType.Authorisation));
        }

        // Check all ClaimRequired attributes (cached per operation type to avoid per-request LINQ allocation)
        var claimAttributes = this.GetClaimAttributes(descriptor);

        foreach (var attribute in claimAttributes)
        {
            var demandedClaim = attribute.GetClaim(resource);

            if (!this._claimInspector.IsDemandedClaimFulfilled(claimsHolder, demandedClaim))
            {
                return Task.FromResult(ExecutionAllowed.No(
                    "insufficient_permissions",
                    $"You do not have the required permission '{attribute.ValueType}' for this resource.",
                    ExecutionAllowedFailureType.Authorisation));
            }
        }

        return ExecutionAllowed.YesTask;
    }

    private ClaimRequiredAttribute[] GetClaimAttributes(ApiOperationDescriptor descriptor)
    {
        return this._claimAttributeCache.GetOrAdd(
            descriptor.OperationType,
            static (_, d) =>
            {
                var attributes = d.TypeAttributes;
                var count = 0;

                for (var i = 0; i < attributes.Count; i++)
                {
                    if (attributes[i] is ClaimRequiredAttribute)
                    {
                        count++;
                    }
                }

                if (count == 0)
                {
                    return [];
                }

                var result = new ClaimRequiredAttribute[count];
                var index = 0;

                for (var i = 0; i < attributes.Count; i++)
                {
                    if (attributes[i] is ClaimRequiredAttribute cra)
                    {
                        result[index++] = cra;
                    }
                }

                return result;
            },
            descriptor);
    }
}
