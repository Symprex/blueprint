using System.ComponentModel.DataAnnotations;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

// Operations and resources shaped as Signature365's are, so that the OpenAPI document legacy Blueprint produced for
// them can be reproduced and pinned: a paged generic resource, a list generic resource, a resource with nested object
// properties (one required, one nullable, one a collection), an enum, a per-type schema name, a command with a body and
// a resource event response.

/// <summary>A tenant, as a partner sees it.</summary>
public partial class LegacyShapeTenantApiResource : ApiResource
{
    public required string TenantId { get; init; }

    public required LegacyShapeBillingAccount BillingAccount { get; init; }

    public LegacyShapeClosedInfo? ClosedInfo { get; init; }

    public LegacyShapeStatus? OwnerStatus { get; init; }

    public LegacyShapeTierKind Tier { get; init; }

    public List<LegacyShapeContact> Contacts { get; init; } = [];
}

/// <summary>A tenant's billing account, only ever nested in another resource.</summary>
public sealed class LegacyShapeBillingAccount
{
    public required string CustomerId { get; init; }

    public LegacyShapeAddress? Address { get; init; }
}

/// <summary>A postal address, nested in a billing account and sent in a command body.</summary>
public sealed class LegacyShapeAddress
{
    public string? Line1 { get; init; }

    public string? PostalCode { get; init; }
}

/// <summary>Why and when a tenant was closed.</summary>
public sealed class LegacyShapeClosedInfo
{
    public required string Reason { get; init; }
}

/// <summary>A tenant contact, nested in a collection.</summary>
public sealed class LegacyShapeContact
{
    public required string Email { get; init; }
}

/// <summary>The status of a tenant's owner.</summary>
public enum LegacyShapeStatus
{
    Active,
    Invited,
}

/// <summary>A tenant's tier, named per type, and used in both a response and a command body.</summary>
[OpenApiSchemaName("LegacyShapeTier")]
public enum LegacyShapeTierKind
{
    Basic,
    Pro,
}

/// <summary>A country a partner can operate in.</summary>
public partial class LegacyShapeCountryApiResource : ApiResource
{
    public required string IsoCode { get; init; }
}

/// <summary>A list resource of the consumer's own, generic over its item.</summary>
/// <typeparam name="T">The item type.</typeparam>
public partial class LegacyShapeListApiResource<T> : ListApiResource<T>
{
    public LegacyShapeListApiResource(IEnumerable<T> values)
        : base(values)
    {
    }

    [System.Text.Json.Serialization.JsonConstructor]
    public LegacyShapeListApiResource()
    {
    }
}

/// <summary>A run whose schema name is given per type, as legacy's <c>[JsonSchema("ImportRun")]</c> gave it.</summary>
[OpenApiSchemaName("LegacyShapeRun")]
public partial class LegacyShapeGatewayRunApiResource : ApiResource
{
    public required string Id { get; init; }
}

/// <summary>Pages through a partner's tenants.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/tenants")]
public partial class GetLegacyShapeTenantsQuery : IQuery<PagedApiResource<LegacyShapeTenantApiResource>>
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public static PagedApiResource<LegacyShapeTenantApiResource> Handle(GetLegacyShapeTenantsQuery query)
    {
        return new PagedApiResource<LegacyShapeTenantApiResource>([], 0, query.PageSize, query.Page);
    }
}

/// <summary>Creates a tenant for a partner.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/partners/{PartnerId}/tenants")]
[HttpPost]
public partial class CreateLegacyShapeTenantCommand : ICommand<ResourceEvent<LegacyShapeTenantApiResource>>
{
    public string PartnerId { get; set; } = null!;

    [Required]
    public string? CompanyName { get; set; }

    public LegacyShapeAddress? Address { get; set; }

    public LegacyShapeTierKind Tier { get; set; }

    public static ResourceEvent<LegacyShapeTenantApiResource> Handle(CreateLegacyShapeTenantCommand command)
    {
        return new ResourceEvent<LegacyShapeTenantApiResource>(
            ResourceEventChangeType.Created,
            "created",
            new LegacyShapeTenantApiResource
            {
                TenantId = "tenant-1",
                BillingAccount = new LegacyShapeBillingAccount { CustomerId = "customer-1", Address = command.Address },
            });
    }
}

/// <summary>Lists the countries a partner can operate in.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/countries")]
public partial class GetLegacyShapeCountriesQuery : IQuery<LegacyShapeListApiResource<LegacyShapeCountryApiResource>>
{
    public static LegacyShapeListApiResource<LegacyShapeCountryApiResource> Handle(GetLegacyShapeCountriesQuery query)
    {
        return new LegacyShapeListApiResource<LegacyShapeCountryApiResource>([]);
    }
}

/// <summary>Gets a run.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/runs/{Id}")]
public partial class GetLegacyShapeRunQuery : IQuery<LegacyShapeGatewayRunApiResource>
{
    public string Id { get; set; } = null!;

    public static LegacyShapeGatewayRunApiResource Handle(GetLegacyShapeRunQuery query)
    {
        return new LegacyShapeGatewayRunApiResource { Id = query.Id };
    }
}
