# Authorization Model

Blueprint provides a compile-time authorization model based on resource scope pairs: a **noun** (the resource type) and a **verb** (the action being performed). At source generation time, operations decorated with `[Resource]` or `[FromResource]` get a static `ScopePair[]` field baked into their pipeline — zero allocation at runtime.

---

## Quick Start

```csharp
// Declare scope requirements on an operation
[Resource("Template", "Read")]
[SelfLink(typeof(TemplateApiResource), "/templates/{Id}")]
[HttpGet]
public partial class GetTemplateQuery : IQuery<OkResult<TemplateApiResource>>
{
    public required string Id { get; set; }
}

// Multiple [Resource] attributes use any-of semantics:
// the caller must satisfy at least one scope pair.
[Resource("Template", "Create")]
[Resource("Template", "Manage")]
[Link(typeof(TemplateApiResource), "/templates")]
[HttpPost]
public partial class CreateTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>
{
    public required string Name { get; set; }
}
```

---

## Attribute Reference

### `[Resource(noun, verb)]`

Declares a resource permission scope requirement. The caller must hold a matching scope claim (e.g. `"Template.Read"` in their JWT).

```csharp
[Resource("Template", "Read")]                          // Simple scope pair
[Resource("Tenant", "Manage", "/Tenants/{Auth:AccountId}")]  // With resource key template
```

| Parameter | Description |
|-----------|-------------|
| `noun` | Resource type (e.g. `"Template"`, `"Tenant"`, `"User"`) |
| `verb` | Action verb (e.g. `"Read"`, `"Create"`, `"Update"`, `"Delete"`, `"Manage"`) |
| `resourceKeyTemplate` | Optional hierarchical resource key for instance-level authorization |

Operations with `[Resource]` **implicitly require authentication** — the pipeline checks `IsAuthenticated` before evaluating scope pairs.

### `[FromResource(typeof(SourceOperation))]`

Inherits scope pairs from another operation. Useful when a mutation's scope requirements match the query that fetches the same resource.

```csharp
[FromResource(typeof(GetTemplateQuery))]                             // Inherit scope pairs
[FromResource(typeof(GetTemplateQuery), "/Tenants/{Auth:AccountId}/Templates/{Id}")]  // Override template
```

- Only direct `[Resource]` declarations on the source type are inherited (not transitive).
- Cannot combine with `[Resource]` on the same operation (compile-time error BPAU005).
- When `resourceKeyTemplate` is provided, it overrides all inherited templates.

---

## Resource Key Templates

Resource key templates enable instance-level authorization. Tokens in the template are expanded at runtime using operation properties and authentication context.

### Token Syntax

| Token | Resolves From | Example |
|-------|---------------|---------|
| `{PropertyName}` | Operation property | `{Id}`, `{TenantId}` |
| `{Auth:AccountId}` | `IUserAuthorisationContext.AccountId` | User's account |
| `{Auth:Id}` | `IUserAuthorisationContext.Id` | User's internal ID |
| `{Auth:IdentityId}` | `IUserAuthorisationContext.IdentityId` | Identity provider ID |
| `{Auth:Email}` | `IUserAuthorisationContext.Email` | User's email |
| `{Auth:Name}` | `IUserAuthorisationContext.Name` | User's display name |
| `{PropertyName:SubProp}` | Property sub-property (e.g. `{Campaign:Id}`) | Nested value access |

### Example

```csharp
[Resource("Template", "Read", "/Tenants/{Auth:AccountId}/Templates/{Id}")]
public partial class GetTemplateQuery : IQuery<OkResult<TemplateApiResource>>
{
    public required string Id { get; set; }
}
```

Generated pipeline code (compile-time):
```csharp
// Static scope pair array (zero allocation per request)
private static readonly ScopePair[] _scopePairs_GetTemplateQuery =
    [new ScopePair("Template", "Read", "/Tenants/{Auth:AccountId}/Templates/{Id}")];

// Expansion at runtime with direct property access (no reflection)
var authorizationDenial = await PipelineHelpers.CheckResourceAuthorizationAsync(
    context,
    httpContext.User,
    this.ResolvedResourceAuthorizationProvider,
    this.ResourceAuthorizationProvider,
    new ResolvedScopePair(
        "Template",
        "Read",
        "/Tenants/{Auth:AccountId}/Templates/{Id}",
        $"/Tenants/{context.UserAuthorisationContext?.AccountId}/Templates/{operation.Id}"),
    _scopePairs_GetTemplateQuery,
    httpContext.RequestAborted);
```

All token expansion uses direct property access — no regex, no reflection, no string manipulation beyond the interpolation.

---

## Authorization Providers

### `IResourceAuthorizationProvider`

The primary authorization provider interface. Receives raw `ScopePair` values and evaluates access.

```csharp
public interface IResourceAuthorizationProvider
{
    Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ScopePair> requiredScopePairs,
        CancellationToken cancellationToken);
}
```

### `IResolvedResourceAuthorizationProvider`

Enhanced interface that receives pre-expanded `ResolvedScopePair` values — the generated pipeline resolves all template tokens before calling this provider.

```csharp
public interface IResolvedResourceAuthorizationProvider
{
    Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
        CancellationToken cancellationToken);
}
```

When both interfaces are registered, the generated pipeline prefers `IResolvedResourceAuthorizationProvider` for template-bearing operations and falls back to `IResourceAuthorizationProvider` for operations without templates.

### `ScopePairClaimAuthorizationProvider`

A production-ready built-in provider that implements both interfaces. It evaluates scope claims from JWT tokens using zero-allocation span-based string comparison.

```csharp
// Default: "scope" claim type, "." separator, ignored scopes for OAuth2
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddResourceAuthorizationProvider<ScopePairClaimAuthorizationProvider>();
});

// Custom configuration with verb hierarchy
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddResourceAuthorizationProvider(
        new ScopePairClaimAuthorizationProvider(
            scopeClaimType: "scope",
            separator: ".",
            ignoredScopes: ["openid", "profile", "email", "Signature365.ReadWrite.All"],
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            }));
});
```

**Verb hierarchy**: When `Manage → [Create, Read, Update, Delete]` is configured, a user with scope `"Template.Manage"` automatically satisfies requirements for `"Template.Create"`, `"Template.Read"`, etc.

**No-API-scope behavior**: When the user's only scopes are ignored scopes (e.g. `openid`, `Signature365.ReadWrite.All`), the `AllowWhenNoScopeClaims` virtual method is called. Override to `true` for full-access semantics (S365 pattern).

---

## Compile-Time Diagnostics

| Diagnostic | Severity | Description |
|------------|----------|-------------|
| BPAU003 | Error | `[Resource]` or `[FromResource]` combined with `[AllowAnonymous]` |
| BPAU004 | Error | Empty or whitespace noun or verb in `[Resource]` |
| BPAU005 | Error | Both `[Resource]` and `[FromResource]` on the same operation |
| BPAU006 | Warning | `[FromResource]` references a type with no `[Resource]` attributes |
| BPAU007 | Error | Unknown token in resource key template |
| BPAU008 | Error | Template references a property that doesn't exist on the operation |

---

## Signature 365 Migration

### Attribute Mapping

```text
S365:                                           Blueprint:
────────────────────────────────────────────    ────────────────────────────────────────────
[Resource("Noun", "Verb", "/path/...")]    →    [Resource("Noun", "Verb", "/path/...")]
[FromResource(typeof(X), "/path")]         →    [FromResource(typeof(X), "/path")]
```

The attribute constructors are identical — no code changes required for `[Resource]` and `[FromResource]` declarations.

### Authorization Provider Mapping

```text
S365:                                           Blueprint:
────────────────────────────────────────────    ────────────────────────────────────────────
ScopeApiAuthoriser : IApiAuthoriser        →    ScopePairClaimAuthorizationProvider
                                                (built-in, or custom IResolvedResourceAuthorizationProvider)

S365ClaimInspector                         →    Generated pipeline code + ScopePairClaimAuthorizationProvider
                                                (template expansion is compile-time, not runtime)

AuthResourceKeyTemplateExpander            →    ResourceKeyTemplateParser (compile-time)
{Auth:AccountId} expansion                 →    Generated inline string interpolation

IS365ResourceKeyExpander                   →    IResolvedResourceAuthorizationProvider.IsAuthorizedAsync
(hierarchical key expansion)                    (consumer implements hierarchical matching)
```

### User Authorization Context

S365's `Signature365UserAuthorisationContext` maps to `IUserAuthorisationContext`:

| S365 Property | Blueprint Property | Notes |
|---------------|-------------------|-------|
| `Id` | `Id` | Direct match |
| `IdentityId` | `IdentityId` | Direct match |
| `TenantId` | — | App-specific, add to your implementation |
| `PartnerId` | — | App-specific, add to your implementation |
| `Email` | `Email` | Direct match |
| `Name` | `Name` | Direct match |
| `AccountId` | `AccountId` | Direct match (`TenantId ?? PartnerId ?? "Symprex"`) |
| `RoleGroup` | — | App-specific |
| `AssignedRoles` | — | App-specific |
| `AssignedScopePairs` | — | App-specific |
| `IsActive` | `IsActive` | Direct match |
| `IsAnonymous` | `IsAnonymous` | Direct match |

### Full S365 Authorization Provider Example

```csharp
/// <summary>
/// S365 authorization provider that evaluates scope-pair claims with verb hierarchy
/// and full-access scope support.
/// </summary>
public class S365AuthorizationProvider : ScopePairClaimAuthorizationProvider
{
    public S365AuthorizationProvider()
        : base(
            scopeClaimType: "scope",
            separator: ".",
            ignoredScopes:
            [
                "openid", "profile", "email", "offline_access",
                "Signature365.ReadWrite.All",
                "Signature365.ReadWrite",
            ],
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            })
    {
    }

    /// <summary>
    /// When the user's only scopes are the ignored full-access scopes,
    /// allow access (S365 behavior: full-access scopes bypass checks).
    /// </summary>
    protected override bool AllowWhenNoScopeClaims(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal)
    {
        return true; // S365 grants access when only ignored scopes are present
    }
}
```

### What S365 Keeps Owning

These remain consumer-level concerns:

- **`Signature365UserAuthorisationContext`** — S365's implementation of `IUserAuthorisationContext` with app-specific properties
- **`IS365ResourceKeyExpander`** — Hierarchical permission expansion (Tenant, Folder, FieldDefinition) is implemented inside the custom `IResolvedResourceAuthorizationProvider`
- **Role-based convenience helpers** — `AuthenticateAsTenantAdministrator`, `AuthenticateAsPartnerUser`, etc. stay in S365's test helpers

### What Blueprint Handles at Compile Time

- Static `ScopePair[]` arrays per operation (zero allocation)
- Template token validation (BPAU diagnostics for invalid tokens)
- Direct property access for template expansion (no reflection)
- Authentication requirement inference from `[Resource]`
- Conflict detection (`[Resource]` + `[AllowAnonymous]`)
- `[FromResource]` scope pair inheritance resolution

---

## Performance Characteristics

| Aspect | Implementation | Cost |
|--------|---------------|------|
| Scope pair storage | `static readonly ScopePair[]` per operation | Zero per-request allocation |
| Template expansion | Inline string interpolation | One string allocation per template per request |
| Scope matching | Span-based ordinal comparison | Zero allocation per comparison |
| Ignored scope check | `FrozenSet<string>` lookup | O(1) per claim |
| Verb hierarchy | `FrozenDictionary<string, string[]>` | O(1) lookup + array scan |
| Provider resolution | Resolved at startup via `PipelineServices` | Zero per-request cost |
