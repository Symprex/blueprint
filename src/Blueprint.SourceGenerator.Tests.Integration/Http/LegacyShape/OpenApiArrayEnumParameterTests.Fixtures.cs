using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

// Mirrors Signature365's Subscriptions GetInvoices query (legacy Spec/openapi.json,
// "/tenants/{TenantId}/invoices".get.parameters, "Statuses"): a query parameter typed as a collection of an enum.

/// <summary>An invoice's status, as Signature365's Subscriptions app declares it.</summary>
public enum ArrayEnumParameterStatus
{
    Paid,
    Posted,
    PaymentDue,
}

/// <summary>How severe an audit finding is.</summary>
public enum ArrayEnumParameterSeverity
{
    Low,
    High,
}

/// <summary>An audit-like resource carrying <see cref="ArrayEnumParameterSeverity"/> as a property of its own, so the enum
/// also has a component through a response, as Signature365's InvoiceStatus does.</summary>
public sealed class ArrayEnumParameterAuditApiResource
{
    public ArrayEnumParameterSeverity Severity { get; init; }
}

/// <summary>An invoice-like resource, so <see cref="ArrayEnumParameterStatus"/> is reached only through the query
/// parameter below, never through a response or request body.</summary>
public sealed class ArrayEnumParameterInvoiceApiResource
{
    public string? Id { get; init; }
}

/// <summary>Filters invoices by an array of statuses, as Subscriptions' <c>GetInvoices</c> does.</summary>
[AllowAnonymous]
[RootLink("/array-enum-parameter/invoices")]
public partial class GetArrayEnumParameterInvoicesQuery : IQuery<ArrayEnumParameterInvoiceApiResource>
{
    public ArrayEnumParameterStatus[]? Statuses { get; set; }

    public static ArrayEnumParameterInvoiceApiResource Handle(GetArrayEnumParameterInvoicesQuery query)
    {
        return new ArrayEnumParameterInvoiceApiResource();
    }
}

/// <summary>Filters audit findings by an array of severities, an enum a response also uses.</summary>
[AllowAnonymous]
[RootLink("/array-enum-parameter/audits")]
public partial class GetArrayEnumParameterAuditsQuery : IQuery<ArrayEnumParameterAuditApiResource>
{
    public ArrayEnumParameterSeverity[]? Severities { get; set; }

    public static ArrayEnumParameterAuditApiResource Handle(GetArrayEnumParameterAuditsQuery query)
    {
        return new ArrayEnumParameterAuditApiResource();
    }
}
