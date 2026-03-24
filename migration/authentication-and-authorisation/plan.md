# Authentication and authorisation

- Status: In progress
- Priority: P1
- Classification: Core middleware
- Depends on: descriptor/auth-metadata design depends on `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, and `../pipeline-composition-and-execution/plan.md`; HTTP runtime execution then also depends on `../http-host-and-message-population/plan.md`

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
- Signature365 descriptor enrichment evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\ApiOperationResourceConvention.cs`
- Signature365 runtime helper evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\ResourceHelpers.cs`
- Signature365 scope authoriser evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\ScopeApiAuthoriser.cs`
- Signature365 claims authoriser evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\S365ClaimsApiAuthoriser.cs`
- Signature365 runtime expansion evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\Expanders\AuthResourceKeyTemplateExpander.cs`
- Signature365 runtime expansion evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\Expanders\PropertyResourceKeyTemplateExpander.cs`
- Signature365 ownership-expansion evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\Blueprint\S365ClaimInspector.cs`
- Signature365 auth test harness: `D:\Projects\Signature365\tests\Symprex.Signature365.TestHelpers\Authorisation\OperationAuthTestFixture.cs`
- Signature365 auth helper path: `D:\Projects\Signature365\tests\Symprex.Signature365.TestHelpers\Authorisation\AuthHelpers.cs`
- Signature365 convention-test evidence: `D:\Projects\Signature365\tests\Symprex.Signature365.TestHelpers.Conventional\Specifications\MustHaveResourceAttributesConventionSpecification.cs`
- Signature365 convention-test evidence: `D:\Projects\Signature365\tests\Symprex.Signature365.TestHelpers.Conventional\Specifications\MustHaveAuthTextFixtureConventionSpecification.cs`
- Signature365 convention-test evidence: `D:\Projects\Signature365\tests\Symprex.Signature365.TestHelpers.Conventional\Specifications\MustHaveScopeOnTargetOfScopeFromOperationSpecification.cs`
- Signature365 access-report tooling: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.ApiOperations\Services\RolesToApiOperationsCreator.cs`
- Usage scan across `D:\Projects\Signature365\src`: `[FromResource(...)]` appears 154 times and `{Auth:...}` placeholders appear 140 times

## Migration goal

Rebuild authentication, user-context loading, and authorisation as source-generated pipeline modules with compile-time validation of the operation contract, compile-time modeling of resource permission templates, and generated auth/access manifests that make policy easier to reason about for humans and AI agents.

## Runtime support to build

- Access to ambient identity (`ClaimsIdentity` or equivalent)
- User-context loading abstraction
- Generated per-operation auth metadata (working name: `AuthorizationDescriptor`) that captures access mode, scope requirements, permission requirements, template bindings, and provenance
- Dual-path authoriser aggregation: JWT scope matching (ScopePair-based, analogous to `ScopeApiAuthoriser`) and resource-key claims matching (template-expand then `IS365ClaimInspector`-style lookup, analogous to `S365ClaimsApiAuthoriser`); the generator must emit distinct expansion code for each path rather than a single generic hook
- Generated pipeline emission driven by `AuthorizationDescriptor`: anonymous operations skip auth entirely, authenticated operations emit only the identity check, and permissioned operations run both the scope path and the claims path in a fixed generator-owned order
- Compile-time resource-permission metadata and generated auth checks
- Generated claim/property expansion for resource templates such as `{Auth:AccountId}` and property placeholders
- Generated claims-checking support for resource-provided pre-expanded keys and for the "no concrete resource instance available" fallback used by link-visibility-style checks
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
- compatibility attributes stay available where they ease migration, but the generator lowers them into explicit analyzable metadata instead of preserving runtime template/reflection behavior
- explicit registration remains for user-context loading, custom authorisers, claim sources, and consumer-owned ownership resolvers
- consumer role/access projection inputs remain consumer-owned; if applications want build-time role-to-operation manifests, that role model must be exposed in a generator-friendly form

Current migration-tooling direction:

- `BPMIG003` now flags parameterless `AddAuthorisation()` activation as redundant safe-deletion cleanup
- `BPMIG101` now flags configured `AddAuthentication(...)` usage as configuration-required migration work because context loading and consumer-owned auth registration still need an explicit seam
- `BPMIG200` now flags legacy `[FromResource(...)]` usage as consumer-extension rewrite migration work and maps it to this workstream plan so auth/resource migration review starts in the shared BPMIG analyzer/report/codemod seam before descriptor-lowering automation exists
- `BPMIG201` now flags raw `{Auth:...}` placeholder usage at the template-literal location, so both auth/resource inheritance and auth-template expansion now enter the same structured review path before descriptor-lowering automation exists
- `Blueprint.Codemod` now carries `BPMIG200` and `BPMIG201` through dry-run/apply reports and `--output <dir>` artifact emission from the shared `MigrationReport`, so the next dependency-safe tooling slice is to rename the shared report family away from activation-only language before more non-activation migration findings land

## Host coverage beyond HTTP

Authentication and authorisation metadata should be host-agnostic even though HTTP is the primary case.

Planned model:

- HTTP hosts provide identity context from `HttpContext`/claims and use the core generated auth stages directly
- alternate hosts may opt into the same generated auth stages when the host contributor can supply an identity or service-principal context
- background-task and message hosts default to anonymous or host-trusted execution unless they explicitly provide identity context and opt into auth checks
- compile-time auth requirements still belong to the operation descriptor regardless of host; the host decides whether it can satisfy them

## Generated auth descriptor

The source-generated model should lower attribute-driven auth into explicit per-operation metadata rather than leaving meaning distributed across runtime helpers and conventions.

That metadata should make at least the following concepts explicit:

- access mode (`Anonymous`, `Authenticated`, `Permissioned`, or close equivalents)
- scope requirement mode, including the current "restrict only when API scopes are present" behavior that `ScopeApiAuthoriser` hides in runtime logic today
- permission requirements as explicit **any-of** resource demands rather than implicit multi-attribute behavior
- resource-template bindings expressed as typed operation-property, actor-context, and literal segments rather than regex/reflection expansion
- provenance showing which requirements were declared directly and which were inherited via `[FromResource(...)]`
- whether a consumer-owned ownership/resource resolver is required at runtime

`AuthorizationDescriptor` is only a working name, but the plan should treat this shape as a first-class deliverable rather than an implementation detail.

Illustrative direction:

```csharp
public sealed record AuthorizationDescriptor(
    AccessMode AccessMode,
    ScopeRequirementMode ScopeRequirementMode,
    ImmutableArray<ResourcePermissionRequirement> AnyOfPermissions,
    bool SupportsPreExpandedResourceKeys,
    bool SupportsGlobalPermissionFallback,
    bool RequiresOwnershipResolver,
    AuthorizationProvenance Provenance);

public sealed record ResourcePermissionRequirement(
    ScopePairDescriptor DeclaredScopePair,
    ImmutableArray<ScopePairDescriptor> EffectiveScopePairs,
    ResourceKeyTemplateDescriptor ResourceKeyTemplate,
    RequirementSource Source,
    string? Details);

public sealed record ResourceKeyTemplateDescriptor(
    string Raw,
    ImmutableArray<ResourceKeySegment> Segments);
```

Descriptor-shape rules:

- each permission alternative must keep the declared scope pair, effective scope-pair closure, resource-key template, and provenance together rather than splitting them into unrelated arrays
- structured resource-key segments should distinguish literal text, operation-property bindings, alternate-property bindings, and actor-context bindings
- `SupportsPreExpandedResourceKeys` covers the `IHaveResourceKeys`-style shortcut path
- `SupportsGlobalPermissionFallback` covers the legacy "no resource instance available" path used by link-visibility-style checks
- manifests, test scaffolding, and generated pipeline code should derive from this descriptor rather than rebuilding auth meaning from attributes or runtime helpers
- the descriptor should stay semantic: do not store raw attributes, runtime `Type` handles, ignored-scope lists, or role-to-operation matrices inside it

## Generated auth/access manifest forms

The descriptor is the canonical per-operation auth model, but the surrounding tooling should emit a small set of deliberate projections rather than one monolithic output.

### 1. Core per-operation auth manifest

Illustrative direction:

- `operations.auth.json` (or an equivalent embedded resource / generated data file)
- one entry per operation
- derived directly from generated `AuthorizationDescriptor` values
- structured data is the canonical source; markdown or human-readable summaries are derived from it

This is the build artifact that humans, AI agents, CI, and snapshot tests should read first.

### 2. Consumer role/access projection

Illustrative direction:

- `roles.access.json` or an equivalent generated data set
- derived from:
  - the core per-operation auth manifest
  - consumer role/service-principal definitions
  - consumer scope and resource-prefix policy

This role-to-operation view is intentionally **not** core Blueprint descriptor data. It is a consumer projection layer that sits on top of the core manifest.

### 3. Runtime and test projections

Runtime diagnostic endpoints, per-user "what can I access?" endpoints, and generated test inputs should all read the generated build artifacts rather than recomputing auth meaning with runtime reflection or DI bootstrapping.

That means:

- runtime diagnostic surfaces expose embedded/generated manifests
- per-user auth-manifest endpoints project from the static manifest plus the current principal, rather than iterating every operation and re-running discovery logic
- snapshot tests and generated matrix tests consume the same manifest data the runtime surfaces expose

The shared artifact naming, schema, and ownership rules for these manifests should align with `../generated-manifests-and-inspection-surfaces/plan.md`.

## Resource permission design

Split the problem into four layers:

1. **Access-mode and requirement-shape modeling in core**
   - anonymous, authenticated, and permissioned execution must be explicit and mutually exclusive
   - multiple `[Resource(...)]` attributes and multiple scope pairs are **any-of** semantics and must be modeled as such rather than staying hidden in authoriser code
2. **Compile-time template binding in core**
   - property placeholders and alternate-property mappings
   - auth placeholders such as `{Auth:AccountId}` and `{Auth:Id}`
   - no regex/reflection expander chain in the hot path
3. **Compile-time permission reuse in core**
   - `[FromResource(...)]` must be resolved by inlining the full `ScopePair[]` (noun+verb tuples) and provenance from the referenced operation's permission metadata at generation time, not by storing a type reference for later reflection
   - `[FromResource(...)]` inherits the target operation's permission/scope metadata, but it keeps the `resourceKeyTemplate` declared on the `FromResource` attribute itself; those are two different metadata sources and the descriptor must model both explicitly
   - `[FromResource(...)]` copies permission requirements only; it must not silently inherit special access modes such as `[AllowAnonymous]` or `[MustBeAuthenticated]`
4. **Consumer-space ownership expansion**
   - DB-backed or hierarchy-aware resource-key expansion remains outside core Blueprint and plugs into a deliberate seam
   - resource-provided pre-expanded keys (`IHaveResourceKeys`-style) should remain a supported shortcut for claims/link checks rather than forcing every path through template expansion or resolver lookups

This keeps the predictable parts source-generated while avoiding hard-coding Symprex-specific ownership lookups into the base library.

## Compile-time checks to add

- conflicting anonymous/authenticated markers
- operations must resolve to one explicit access mode
- permission metadata applied to operations that allow anonymous access without an explicit reason
- missing user-context loader when operations require authenticated user context
- unsupported custom permission or claim attribute usage
- invalid tenant/resource permission property references where these can be known statically
- `[FromResource(...)]` and `[Resource(...)]` applied to the same operation
- multiple `[FromResource(...)]` attributes applied to the same operation
- `[FromResource(...)]` referencing an operation that does not expose compatible resource metadata or resolves only to a special-permission access mode
- resource templates that reference missing auth claims or missing operation/resource properties
- unknown auth placeholders or unsupported placeholder forms
- operations that rely on generated tenant/auth hydration without exposing a compatible target property or mapping
- ownership-expansion scenarios that require a consumer resolver but none is configured
- operations that need the no-resource/global-permission fallback or pre-expanded-resource-keys shortcut but do not expose the required generated descriptor flags
- non-HTTP hosts exposing auth-protected operations without an identity-supply strategy
- descriptor metadata that hides any-of permission/scope semantics instead of surfacing them explicitly
- consumer role/access projections requested without a generator-analyzable role/service-principal model

## Testing and access reporting

Signature365 currently splits auth confidence across:

- convention tests that check whether operations are marked correctly
- `OperationAuthTestFixture<T>` matrix tests that check role/scenario behavior
- separate scope-authoriser and end-to-end API tests that exercise the wider pipeline

The rewrite should improve that story rather than pretending one artifact can replace every layer.

Planned direction:

- replace convention-style completeness checks with analyzers
- generate per-operation auth/access manifests and consumer role/access projections as first-class artifacts rather than as separate tools
- use those artifacts to drive descriptor snapshot tests and generated parameterized matrix tests for the mechanical role/principal × operation coverage
- include service-principal or agent scenarios in the generated matrix layer when the consumer role/access projection defines them
- keep targeted end-to-end and scenario-boundary tests for the full host pipeline, scope-handling behavior, custom authorisers, consumer-owned ownership/resource resolvers, and tenant/partner/self-resource ownership rules
- make the generated manifests readable enough that humans and AI agents can answer "who can call this and why?" without reverse-engineering runtime helpers
- make the descriptor itself snapshot-friendly so tests can assert auth shape directly without reproducing convention logic
- make the shared generated auth test harness execute the full generated auth plan (scope path plus claims path), not just the claims path

## Phase 1 scope boundary

The first implementation pass should stay focused on the core auth contract and generated execution path.

Phase 1 scope:

- `AuthorizationDescriptor` (or final equivalent)
- compile-time lowering of auth/resource metadata
- generated auth stages in the pipeline plan
- `operations.auth`-style per-operation manifest output
- compile-time `[FromResource(...)]` modeling

Later or separate projection work:

- consumer role/service-principal projection layers
- `roles.access`-style derived manifests
- generated consumer-specific access-matrix scaffolding
- successors to broad consumer reporting tools such as `RolesToApiOperationsCreator`

That split keeps the core auth work from being blocked on projection/reporting decisions that sit one layer above it.

## Observability and debuggability

- generated `operations.auth` build artifact per app/assembly
- generated consumer `roles.access` projection when role configuration is available at build time
- generated auth requirement summary per operation
- generated role-to-operation / role-to-access manifest (successor to `RolesToApiOperationsCreator`)
- snapshot-friendly auth/access manifest that can feed parameterized tests
- denial diagnostics that name the requirement source
- activity/log tags for authenticated user context
- permission-template manifest showing where each resource requirement came from
- explicit tags for generated tenant/resource-key hydration

## Legacy artifact disposition

Planned replacements:

- `ResourcePermissionsFeatureData` and `ApiOperationResourceConvention` runtime auth reconstruction
- auth-related convention specifications
- `RolesToApiOperationsCreator`, `RoleMappersToApiOperationsCreator`, `OperationMapperLoader`, and markdown-only access-report tooling
- mechanical leaf `*AuthTestFixture` classes whose only job is to encode role/principal allowlists the generator can derive from manifests

Planned survivors or migrated equivalents:

- generated equivalents of the scope path and claims path
- consumer-owned ownership/resource-key resolver seams
- targeted scenario-boundary auth tests
- migration-time compatibility for ignored scopes and legacy scope aliases

## Explicit decisions

- Split authentication and authorisation into separate stage contributors even if configuration exposes them together.
- Name a concrete generated per-operation auth metadata type (candidate: `AuthorizationDescriptor`) that captures inlined ScopePairs, resource-key templates, auth placeholder bindings, and provenance; this type does not yet exist in Signature365 and is a design decision to make during implementation.
- Treat authentication and authorisation as always architecturally available capabilities that compile away when unused.
- Make compile-time expansion of auth requirements into descriptor metadata and pipeline plans part of core Blueprint.
- Make `[FromResource(...)]` resolution a compile-time feature, not a runtime reflection story.
- Make `[FromResource(...)]` model both inherited permission metadata and its own declared resource-key template explicitly.
- Make `[FromResource(...)]` copy permission requirements only; do not let it silently carry special access-mode semantics.
- Keep the generated auth descriptor semantic and paired; do not recreate legacy `ResourcePermissionsFeatureData` as loose arrays of templates and scope pairs.
- Prefer generated tenant/context hydration over a standalone `SetTenantMiddleware` where the mapping is statically knowable.
- Prefer generated property/actor binders over regex- and reflection-driven template expansion.
- Make access-mode, scope behavior, and any-of permission semantics explicit in generated metadata rather than implicit in authoriser implementations.
- Keep legacy ignored-scope lists and scope-alias compatibility (`LegacyScopeMapper`-style behavior) in generated code or migration shims, not as descriptor data.
- Permissioned operations require both the scope path and the claims path; the generator owns the canonical authoriser ordering.
- Emit structured auth/access build artifacts as the canonical source of truth; runtime endpoints, human-readable summaries, and tests project from those artifacts instead of recomputing auth meaning.
- Keep role/service-principal access views as consumer projections over the core auth manifest rather than bloating the core descriptor with consumer policy.
- Use generated auth/access manifests to replace convention-style completeness tests and to feed generated matrix tests plus parameterized auth tests, but keep targeted end-to-end auth tests for full-pipeline integration behavior.
- Include `[FromResource(...)]` operations in generated manifest/test rows even when they no longer require hand-authored leaf fixtures.
- Keep DB-backed ownership/resource-key expansion in consumer space.
- Prefer explicit `TenantId`-style properties and generated hydration over magic runtime resource-key expanders.

## Sequencing

Implement after HTTP, because most auth behavior depends on host/request context and operation exposure metadata.
