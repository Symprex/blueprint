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

### Reading the declarations at runtime

The generator records what these attributes declare on each operation's descriptor, so an app that needs it, to map roles to operations or list an operation's permissions, reads it there instead of reflecting over the attributes:

| Feature data | Present when | Carries |
|---|---|---|
| `OperationAccessFeatureData` | An access attribute is declared | `Mode`: `Anonymous` (`[AllowAnonymous]`), `Authenticated` (`[MustBeAuthenticated]` with no scope pairs) or `Permissioned` (`[Resource]`/`[FromResource]`, which wins over `[MustBeAuthenticated]`) |
| `OperationAuthorizationFeatureData` | The operation is permissioned | `ScopePairs`, each with its effective template; `InheritedFrom`, the `[FromResource]` source operation; `InheritedTemplateOverride`, the template `[FromResource]` gave |
| `OperationAuthenticationFeatureData` | The pipeline checks identity | `Requirement`: what is enforced, which for an HTTP operation that declares nothing is `Authenticated` by default |

```csharp
if (descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var authorization))
{
    var pairs = authorization.ScopePairs;          // any-of
    var source = authorization.InheritedFrom;       // null for a direct [Resource]
}
```

`OperationAccessFeatureData` is absent for an operation that declares no access attribute; use it, not `OperationAuthenticationFeatureData`, to tell `[MustBeAuthenticated]` from no declaration.

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
// Inline expansion at runtime with direct property access (no reflection, no static field)
var authorizationDenial = await PipelineHelpers.CheckResourceAuthorizationAsync(
    context,
    httpContext.User,
    this.ResourceAuthorizationProviders,
    new ResolvedScopePair(
        "Template",
        "Read",
        "/Tenants/{Auth:AccountId}/Templates/{Id}",
        $"/Tenants/{context.UserAuthorisationContext?.AccountId}/Templates/{operation.Id}"),
    httpContext.RequestAborted);
```

All token expansion uses direct property access — no regex, no reflection, no string manipulation beyond the interpolation.

---

## Authorization Provider

### `IResourceAuthorizationProvider`

The single authorization interface. Receives pre-expanded `ResolvedScopePair` values — the generated pipeline resolves all template tokens before calling the provider.

```csharp
public interface IResourceAuthorizationProvider
{
    ValueTask<ExecutionAllowed> AuthorizeAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        ResourceAuthorizationRequest request,
        CancellationToken cancellationToken);
}
```

`ResourceAuthorizationRequest` carries the operation being authorised (`Descriptor`), its `ScopePairs`, and the `Mode` the provider is asked in: `Execute`, `Link` with the linked `Resource`, or `Listing` when an operation is listed before any resource exists (an authorisation manifest). Read `request.Descriptor`, not the context's: when links are written, the context is the operation that returned the resource, and the request's descriptor is the linked one.

### Link visibility

Whether a link to a permissioned operation is written on a resource is decided by the same providers (Symprex/blueprint#79). `IApiAuthoriserAggregator.CanShowLinkAsync` asks them all-of, in `Link` mode:
- after the `IApiAuthoriser`s and before the `ILinkVisibilityAuthoriser`s;
- with the caller's `ApiOperationContext.UserPrincipal`, which the HTTP path sets from `HttpContext.User`;
- with the operation's keys resolved against the **resource**, by generated code.

For each resource type an operation links from (`[Link]`, `[SelfLink]`), the generator emits a resolver into the operation's descriptor, as `OperationLinkAuthorizationFeatureData`. The resolver reads the resource's properties directly, so a list's items are each decided by their own keys. Against a resource:
- `{A:B}` uses `A` when the resource type has a public property `A`, and `B` only when it does not. Whether the property exists decides, not whether its value is null, as Signature 365 decides it.
- A null value is written as an empty segment.
- `{Auth.X}` resolves as it does at execution: from the user's context for the properties it has, otherwise from the claim of that name.
- A token that matches no property of a linked resource type is a compile-time error (`BPAU011`).

A link to a permissioned operation is hidden when no resolver handles the resource's type, or no provider is registered, as its execution would be refused. Providers are not asked about an operation without scope pairs.

### Listing

An authorisation manifest lists the operations a caller may reach before any resource exists, by asking `IApiAuthoriserAggregator.CanShowLinkAsync` with no resource. The providers are then asked in `Listing` mode (Symprex/blueprint#81). The generated `OperationListingAuthorizationFeatureData` resolves the caller's `{Auth.X}` tokens as an execution does and cuts each key at its first property token, so `/Tenants/{Auth.AccountId}/Users/{Id}` is listed as the prefix `/Tenants/ten_1/Users/`. `ResourceKeyAuthorizationProvider` matches grants against a cut key prefix-compatibly, which accepts two shapes execution never honours (an exact ancestor such as `/Partners/p` for `/Partners/p/Users/`, and a grant beneath the prefix that diverges from the rest of the template), so an operation can be listed and still refused. A key with no property token is complete, and is judged as at execution. Listing mode is reached only from the aggregator: an execution is always asked in `Execute` mode.

Each `ResolvedScopePair` carries the raw template (`/Tenants/{Auth:AccountId}/Templates/{Id}`) alongside the expanded resource key (`/Tenants/abc123/Templates/42`), giving providers full context for both pattern-based and instance-level authorization decisions.

**A decision, not a yes or no.** A provider returns `ExecutionAllowed.Yes` or a denial made with `ExecutionAllowed.No(reason, message, failureType)`, and the denial is answered exactly as an `IApiAuthoriser`'s is:
- `ExecutionAllowedFailureType.Authorisation` is a `403`, any other a `401`;
- the `message` titles the problem response;
- the `reason` is logged at Information (`Permission check failed. reason=… authoriser=<provider type>`) and never written, so it may name claims and keys the caller must not see.

**Every registered provider must allow.** Providers compose all-of, like authorisers: they are asked in registration order and the first denial is the answer, so the rest are not asked. Register one provider per concern, for example one for a token's OAuth scopes and one for the caller's grants on the resource, rather than one provider that composes both.

> **Breaking change (Symprex/blueprint#77):** this replaced `Task<bool> IsAuthorizedAsync(…, IReadOnlyList<ResolvedScopePair>, …)`. A provider's `false` was always a plain `403` titled `Forbidden`; a denial is now titled with its message. Registering more than one provider used to keep only the last; now every one is asked.

**A provider is required once any operation declares scope pairs.** Without one, the checks fail closed.
- `MapBlueprintApi` throws `InvalidOperationException` at start-up, naming the operations.
- `PipelineHelpers.CheckResourceAuthorizationAsync` refuses each one with a plain `403` and logs at Error (`NoResourceAuthorizationProvider`, event 33). This catches a host that never calls `MapBlueprintApi`, such as an in-memory test host.
- Before Symprex/blueprint#41 the check passed whenever no provider was registered, letting any authenticated caller through.
- An app with no `[Resource]`/`[FromResource]` operations needs no provider.
- To let every caller through deliberately, register a provider that returns `ExecutionAllowed.Yes`.

### In-process executions

Providers are asked wherever an `IApiAuthoriser` is (Symprex/blueprint#78), not only for HTTP requests. An operation with a `[Resource]` or `[FromResource]` and no link is executed in process only, with no `HttpContext`: an unlinked operation, a Rebus message, a background task or a nested execution (`CreateNested`, `ExecuteWithNewScopeAsync`). Every permissioned pipeline, HTTP or not, calls the same `PipelineHelpers.CheckOperationResourceAuthorizationAsync` (all-of providers, fail closed with none registered):
- the principal is `context.UserPrincipal`, which over HTTP is the request's user and in a nested context is the parent's; the cancellation is `context.OperationCancelled`, which over HTTP is the request's;
- with no authenticated principal the caller is refused as anonymous (`UnauthenticatedResult`) and no provider is asked, because a provider judges a principal's claims (over HTTP the authentication step has already refused such a caller);
- nothing is asked when `context.SkipAuthorisation` is set, as the authentication and authoriser steps do not run either; or, for an operation with no link, when its descriptor allows anonymous access, as the authorisers are not run. An operation with a link is still checked when its descriptor allows anonymous access: a Rebus consumer marks a subscribed operation's descriptor anonymous, but `MapBlueprintApi` still routes the operation when it has a URL. The generated pipeline knows which it is, so a nested execution, which inherits the request's `HttpContext`, is judged by its own exposure.

A descriptor allows anonymous access when it is `[AllowAnonymous]`, a background task, or a message a Rebus consumer subscribes to (marked when the app is made). So a subscribed message's `[Resource]` is not checked on delivery, as its authorisers are not run; the same operation executed with no such marking is.

> **Breaking change (Symprex/blueprint#78):** an in-process execution of a permissioned operation used to skip the provider, and `[Resource]` on a non-HTTP operation only drew BPAU008. It is now enforced: an app with such operations needs a registered provider and an authenticated `UserPrincipal` on the context, or the execution is refused.

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

**Denials**: every denial is a `403` titled `ScopePairClaimAuthorizationProvider.DeniedMessage` ("You do not have enough permissions to perform this operation"). Its logged reason names the scope pairs required and the API scope claims the caller held.

**Verb hierarchy is transitive**: with `Admin → [Manage]` and `Manage → [Read]`, a user with `Template.Admin` satisfies `Template.Read`. Blueprint computes the closure once, at construction, and tolerates cycles.

**Exact ignored scopes**: the full constructor uses `ignoredScopes` exactly as given. Only the parameterless constructor and `WithCrudVerbHierarchy` add the OAuth2 defaults (`openid`, `profile`, `email`, `offline_access`), so leave a scope out of the set to have it count as an API scope.

**Unauthenticated callers**: refused by default. Pass `allowUnauthenticated: true` when another provider registered beside this one refuses them itself, for example with a `401`, so this one does not answer first with a `403`.

**No-API-scope behavior**: When the user's only scopes are ignored scopes (e.g. `openid`, `Signature365.ReadWrite.All`), the `AllowWhenNoScopeClaims` virtual method is called. Override to `true` for full-access semantics (S365 pattern).

### `ResourceKeyAuthorizationProvider`

A built-in provider for instance-level authorisation (Symprex/blueprint#80). It decides each scope pair by whether the caller holds a grant covering the pair's resource key, such as "may rename folders under `/Accounts/acc_1`". `ScopePairClaimAuthorizationProvider` checks noun and verb only; register both when you need both, and each must allow.

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint => blueprint
    .AddResourceAuthorizationProvider(ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy()) // the token's scopes
    .AddResourceKeyAuthorization()                                                                  // the caller's grants
    .AddResourceGrantSource(new ScopePairClaimResourceGrantSource("grant", "::"))
    .AddResourceKeyExpander<FolderKeyExpander>());
```

`AddResourceKeyAuthorization()` registers the provider, a singleton, once however often it is called, and the default `IResourceAccessChecker`, scoped, unless one is registered already. `AddResourceGrantSource<T>()` and `AddResourceKeyExpander<T>()` register scoped; `AddResourceGrantSource(instance)` registers a configured source as a singleton.

**Keys and grants.** A `ResourceKey` is a key being asked about: absolute when it starts with `/`, otherwise partial. A `ResourceGrant` is a key held, and is always absolute:
- an exact grant covers its own key alone;
- a grant ending in `/*` covers its base and every key beneath it, at a segment boundary only, so `/Accounts/acc_1/*` covers `/Accounts/acc_1/Folders/f1` and never `/Accounts/acc_12`;
- `/*` alone is the root grant, which covers every absolute key;
- comparison is ordinal, and there is no global key.

**Deciding a request.** In `Execute` and `Link` modes the request is allowed when any scope pair is. For each pair:
1. Its resolved key is demanded, as a `ResourceDemand` of `request.Descriptor` (for a link, the linked operation) and the key.
2. In `Link` mode, when the resource implements `IHaveResourceKeys` and the key is partial, the resource's own key that ends with it is demanded instead. The comparison is case-insensitive, and when several keys match, the last one wins, as in Signature 365.
3. A partial key goes through the `IResourceKeyExpander`s in registration order, and the first answer that is not `null` is its expansion. When none handles it, the pair is denied with a reason naming the key, and a warning is logged: a grant can only ever cover an absolute key.
4. The grants of every `IResourceGrantSource` are combined, and the pair is allowed when any grant covers any of its keys.

**Refusals.**

| Case | Result |
|------|--------|
| The principal is unauthenticated, or the user context is anonymous | `401`, "Please log in" |
| The user context is not active | `401`, "Your account has been deactivated" |
| A pair has no resource key, or its key resolved empty | That pair is denied |
| No pair is satisfied, or the request has no pairs | `403`, `ResourceKeyAuthorizationProvider.DeniedMessage` |

A caller with no user context at all, as in an app without an `IUserAuthorisationContextFactory`, is judged by grants. Each reason is logged, never written.

**Grant sources.** `IResourceGrantSource.GetGrantsAsync(context, principal, operation, scopePair, ct)` returns the `ResourceGrants` the caller holds. It is given both the operation and the scope pair, so a source keyed by either fits. The built-in `ScopePairClaimResourceGrantSource(claimType, separator)` reads claims of that type whose value is `{resourceKey};{Noun}{separator}{Verb}`, such as `/Accounts/acc_1/*;Folder::Rename`:
- noun and verb match case-insensitively, and no verb implies another;
- only the claims of authenticated identities are read;
- a value without exactly one `;`, for another pair, or whose key does not start with `/` is skipped, never thrown on.

**Expanders.** `IResourceKeyExpander.ExpandAsync(partialKey, context, ct)` returns the absolute keys a partial key stands for, an empty list for a key it handles that identifies nothing (which is then denied), or `null` when the key is not its to expand.

**Checking access directly.** `IResourceAccessChecker.HasAccessAsync(context, principal, demand, scopePair, ct)` makes the decision the provider makes for each pair, without the authentication checks. A handler that must filter by what the caller may do asks it directly:

```csharp
public static async Task<FolderListApiResource> HandleAsync(GetFoldersQuery query, IResourceAccessChecker checker, ApiOperationContext context)
{
    var create = context.App.FindOperation(typeof(CreateFolderCommand))!;
    var pair = create.GetFeatureData<OperationAuthorizationFeatureData>()!.ScopePairs[0];
    var key = $"/Accounts/{context.UserAuthorisationContext!.AccountId}";

    var canCreate = await checker.HasAccessAsync(
        context,
        context.UserPrincipal!,
        new ResourceDemand(create, new ResourceKey(key)),
        new ResolvedScopePair(pair.Noun, pair.Verb, pair.ResourceKeyTemplate, key),
        context.OperationCancelled);

    // ...
}
```

---

## Compile-Time Diagnostics

| Diagnostic | Severity | Description |
|------------|----------|-------------|
| BPAU003 | Error | `[Resource]` or `[FromResource]` combined with `[AllowAnonymous]` |
| BPAU004 | Error | Empty or whitespace noun or verb in `[Resource]` |
| BPAU005 | Error | Both `[Resource]` and `[FromResource]` on the same operation |
| BPAU006 | Warning | `[FromResource]` references a type with no `[Resource]` attributes |
| BPAU007 | Error | Unknown token in resource key template |
| BPAU008 | Warning | `[Resource]` or `[FromResource]` on a background task that is not exposed over HTTP and not marked `[MustBeAuthenticated]`: it allows anonymous access, so the declaration is never enforced |

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
                                                (built-in, or custom IResourceAuthorizationProvider)

S365ClaimsApiAuthoriser                    →    Generated pipeline code + ResourceKeyAuthorizationProvider
(with S365ResourceAccessChecker)                (template expansion is compile-time, not runtime)

AuthResourceKeyTemplateExpander            →    ResourceKeyTemplateParser (compile-time)
{Auth:AccountId} expansion                 →    Generated inline string interpolation

IS365ResourceKeyExpander                   →    IResourceKeyExpander
(hierarchical key expansion)

IS365ResourceAccessChecker                 →    IResourceAccessChecker

IS365ResourceGrantsHolder                  →    IResourceGrantSource, keyed by operation type
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

The recommended approach uses the `WithCrudVerbHierarchy` factory method, which pre-configures the
standard CRUD verb hierarchy (`Manage → [Create, Read, Update, Delete]`):

```csharp
// In Startup.ConfigureServices() — preferred factory approach
services.AddBlueprintApi<PortalBlueprintApp, PortalBlueprintAppExecutor>(blueprint =>
{
    blueprint.AddResourceAuthorizationProvider(
        ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: "::",              // S365 uses :: as the scope separator
            legacyScopeMapper: null,
            allowWhenNoScopeClaims: true,  // S365 grants full access when no restrictive scopes
            additionalIgnoredScopes:
            [
                "Signature365.ReadWrite.All",
                "Signature365ReadWrite",
                "Signature365Admin.ReadWrite.All",
                "Signature365Admin.ReadWrite",
                "Signature365Partner.ReadWrite",
            ]));
});
```

Alternatively, subclass for app-specific overrides:

```csharp
public class S365AuthorizationProvider : ScopePairClaimAuthorizationProvider
{
    public S365AuthorizationProvider()
        : base(
            scopeClaimType: "scope",
            separator: "::",
            ignoredScopes:
            [
                "openid", "profile", "email", "offline_access",
                "Signature365.ReadWrite.All",
                "Signature365ReadWrite",
            ],
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            },
            allowWhenNoScopeClaims: true)
    {
    }
}
```

### What S365 Keeps Owning

These remain consumer-level concerns:

- **`Signature365UserAuthorisationContext`** — S365's implementation of `IUserAuthorisationContext` with app-specific properties
- **Grants and key expanders** — the builder of each user's per-operation grants, supplied as an `IResourceGrantSource`, and the expanders (Tenant, Folder, FieldDefinition), supplied as `IResourceKeyExpander`s. The matching is `ResourceKeyAuthorizationProvider`'s
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
