using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Middleware;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates the <see cref="IPropertyPopulator"/> seam by auto-populating <c>TenantId</c>
/// properties from JWT claims — the compile-time equivalent of Signature365's
/// <c>SetTenantMiddleware</c>.
/// <para>
/// This populator runs in the Population pipeline stage after HTTP binding, so route/body/query
/// values are already set. It only fills <c>TenantId</c> when the property is null or empty,
/// preserving any explicit caller-provided values (which the authorization layer can then verify).
/// </para>
/// </summary>
public sealed class TenantPropertyPopulator : IPropertyPopulator
{
    /// <summary>
    /// The claim type used to extract the tenant ID from JWT tokens.
    /// In Signature365, this is <c>Sig365JwtClaimTypes.Sig365TenantId</c>.
    /// </summary>
    public const string TenantIdClaimType = "tenant_id";

    private readonly ITenantAccessor _tenantAccessor;
    private readonly ILogger<TenantPropertyPopulator> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="TenantPropertyPopulator"/> class.
    /// </summary>
    /// <param name="tenantAccessor">The ambient tenant context.</param>
    /// <param name="logger">The logger.</param>
    public TenantPropertyPopulator(ITenantAccessor tenantAccessor, ILogger<TenantPropertyPopulator> logger)
    {
        this._tenantAccessor = tenantAccessor;
        this._logger = logger;
    }

    /// <inheritdoc />
    public Task PopulateAsync(ApiOperationContext context, CancellationToken cancellationToken)
    {
        var identity = context.UserIdentity;

        if (identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var tenantClaim = identity.FindFirst(TenantIdClaimType);

        if (tenantClaim is null)
        {
            return Task.CompletedTask;
        }

        // Set the ambient tenant context for the duration of this operation
        this._tenantAccessor.Current = tenantClaim.Value;

        // Auto-populate TenantId properties on the operation, respecting existing values
        var operation = context.Operation;
        var operationType = operation.GetType();

        var tenantIdProp = operationType.GetProperty("TenantId");

        if (tenantIdProp is null || !tenantIdProp.CanWrite)
        {
            return Task.CompletedTask;
        }

        var existingValue = tenantIdProp.GetValue(operation) as string;

        if (!string.IsNullOrEmpty(existingValue))
        {
            return Task.CompletedTask;
        }

        tenantIdProp.SetValue(operation, tenantClaim.Value);

        this._logger.LogDebug(
            "Auto-populated TenantId on {OperationType} from claim '{ClaimType}'",
            operationType.Name,
            TenantIdClaimType);

        return Task.CompletedTask;
    }
}
