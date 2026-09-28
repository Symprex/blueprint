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
