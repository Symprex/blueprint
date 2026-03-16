# Authentication and authorisation

- Status: Planned
- Priority: P1
- Classification: Core middleware
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`, `../http-host-and-message-population/plan.md`

## Why this module exists

The legacy Blueprint pipeline has distinct authentication and authorisation stages, supports user-context loading, and allows operation-level attributes to control anonymous access, required claims, and permission checks.

The source-generated rewrite must preserve those semantics while moving decision-making and diagnostics earlier.

## Evidence

- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Authorisation\AuthenticationMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Authorisation\UserContextLoaderMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Authorisation\AuthorisationMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\BuiltinBlueprintMiddlewares.cs`
- Generated evidence: `D:\Projects\Signature365\src\Apps\AdminPortal\Symprex.Signature365.AdminPortal\Internal\Generated\Blueprint\AdminPortalCreateLicenseKeyCommandExecutorPipeline.cs`
- Signature365 extension evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\SetTenantMiddleware.cs`
- Signature365 auth model: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\ResourceAttribute.cs`
- Signature365 auth model: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\FromResourceAttribute.cs`
- Signature365 runtime expansion evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\Expanders\AuthResourceKeyTemplateExpander.cs`
- Signature365 runtime expansion evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\Expanders\PropertyResourceKeyTemplateExpander.cs`
- Signature365 ownership-expansion evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\S365ClaimInspector.cs`
- Usage scan across `D:\Projects\Signature365\src`: `[FromResource(...)]` appears 154 times and `{Auth:...}` placeholders appear 140 times

## Migration goal

Rebuild authentication, user-context loading, and authorisation as source-generated pipeline modules with compile-time validation of the operation contract and compile-time modeling of resource permission templates.

## Runtime support to build

- Access to ambient identity (`ClaimsIdentity` or equivalent)
- User-context loading abstraction
- Authoriser aggregation and operation-specific requirements
- Compile-time resource-permission metadata and generated auth checks
- Generated claim/property expansion for resource templates such as `{Auth:AccountId}` and property placeholders
- A narrow consumer-owned ownership/resource resolver seam for DB-backed or hierarchy-aware permission expansion

## Consumer configuration and activation

Preserve the important shapes:

- user-context loader registration
- custom authoriser registration
- `[AllowAnonymous]`
- `[MustBeAuthenticated]`
- `[ClaimRequired(...)]`
- `[Resource(...)]`
- `[FromResource(...)]`
- permission attributes or equivalent permission metadata

Long-term target:

- auth and authorisation stages are architecturally available and compile away when unused
- `.AddAuthentication(...)` and `.AddAuthorisation()` become configuration hooks or migration shims, not the feature-activation model
- explicit registration remains for user-context loading, custom authorisers, claim sources, and consumer-owned ownership resolvers

## Host coverage beyond HTTP

Authentication and authorisation metadata should be host-agnostic even though HTTP is the primary case.

Planned model:

- HTTP hosts provide identity context from `HttpContext`/claims and use the core generated auth stages directly
- alternate hosts may opt into the same generated auth stages when the host contributor can supply an identity or service-principal context
- background-task and message hosts default to anonymous or host-trusted execution unless they explicitly provide identity context and opt into auth checks
- compile-time auth requirements still belong to the operation descriptor regardless of host; the host decides whether it can satisfy them

## Resource permission design

Split the problem into three layers:

1. **Compile-time template expansion in core**
   - property placeholders and alternate-property mappings
   - auth placeholders such as `{Auth:AccountId}` and `{Auth:Id}`
2. **Compile-time permission reuse in core**
   - `[FromResource(...)]` should resolve against the referenced operation's permission metadata at generation time
3. **Consumer-space ownership expansion**
   - DB-backed or hierarchy-aware resource-key expansion remains outside core Blueprint and plugs into a deliberate seam

This keeps the predictable parts source-generated while avoiding hard-coding Symprex-specific ownership lookups into the base library.

## Compile-time checks to add

- conflicting anonymous/authenticated markers
- permission metadata applied to operations that allow anonymous access without an explicit reason
- missing user-context loader when operations require authenticated user context
- unsupported custom permission or claim attribute usage
- invalid tenant/resource permission property references where these can be known statically
- `[FromResource(...)]` referencing an operation that does not expose compatible resource metadata
- resource templates that reference missing auth claims or missing operation/resource properties
- operations that rely on generated tenant/auth hydration without exposing a compatible target property or mapping
- ownership-expansion scenarios that require a consumer resolver but none is configured
- non-HTTP hosts exposing auth-protected operations without an identity-supply strategy

## Observability and debuggability

- generated auth requirement summary per operation
- denial diagnostics that name the requirement source
- activity/log tags for authenticated user context
- permission-template manifest showing where each resource requirement came from
- explicit tags for generated tenant/resource-key hydration

## Explicit decisions

- Split authentication and authorisation into separate stage contributors even if configuration exposes them together.
- Treat authentication and authorisation as always architecturally available capabilities that compile away when unused.
- Make compile-time expansion of auth requirements into descriptor metadata and pipeline plans part of core Blueprint.
- Make `[FromResource(...)]` resolution a compile-time feature, not a runtime reflection story.
- Prefer generated tenant/context hydration over a standalone `SetTenantMiddleware` where the mapping is statically knowable.
- Keep DB-backed ownership/resource-key expansion in consumer space.
- Prefer explicit `TenantId`-style properties and generated hydration over magic runtime resource-key expanders.

## Sequencing

Implement after HTTP, because most auth behavior depends on host/request context and operation exposure metadata.
