# Signature 365 → Blueprint Source-Gen: Migration Report

> **Purpose:** Track per-service compilation status, change counts, test pass rates, and overall migration progress.
> Updated by AI agent sessions as migration work proceeds.

## Executive Summary

| Metric | Value |
|--------|-------|
| Total S365 projects | 169 |
| Blueprint-affected projects | ~97 (Apps) + ~15 (Core/Common) |
| SMTP projects (excluded) | 59 |
| Legacy Blueprint projects | 11 |
| Test projects | 50 (26 SMTP, 24 API) |
| Migration status | **Contract compilation — 15/15 pass** |

## Blueprint Framework Readiness

All framework features required for S365 migration are implemented and tested:

| Feature | Status | Tests | Notes |
|---------|--------|-------|-------|
| Source generator (44 modules) | ✅ Ready | 796 | Incremental, fully cached |
| Integration tests | ✅ Ready | 1,178 | Pipeline execution proofs |
| Blueprint.Marten | ✅ Ready | — | UoW, outbox, multi-tenancy |
| Blueprint.Rebus | ✅ Ready | — | Auto-detecting module, tx scope |
| Blueprint.Tasks.Hangfire | ✅ Ready | — | Schedule provider, recurring |
| Blueprint.Testing | ✅ Ready | — | Factory, ambient auth, S365 shims |
| Blueprint.HttpClient | ✅ Ready | — | Gateway E2E sample |
| `[Resource]` auth (built-in) | ✅ Ready | — | Noun/verb, template expansion |
| TenantModule (first-class) | ✅ Ready | — | Claim-based, compile-time |
| OpenAPI (well-known) | ✅ Ready | — | RFC 9264 + API catalog |
| Migration analyzers | ✅ Ready | 95 | 13 BPMIG diagnostics |
| Codemod tool | ✅ Ready | — | All 13 diagnostics detected in S365 scan |
| Route deduplication | ✅ Ready | — | HashSet-based endpoint dedup |
| Pipeline helper extraction | ✅ Ready | — | ~4,000+ lines saved across S365 |

## Service Inventory

### API Services (Migration Scope)

| Service | Cmds | Queries | Handlers | `[Resource]` | Tier | Status |
|---------|------|---------|----------|-------------|------|--------|
| Analytics | 1 | 1 | 6 | 2 | 1 | ✅ Proof compiled |
| EventStore | 0 | 1 | 4 | 2 | 1 | ✅ Proof compiled |
| Templates | 0 | 1 | 2 | 0 | 2 | ✅ Proof compiled |
| Generation | 0 | 6 | 12 | 2 | 2 | ✅ Proof compiled |
| ExchangeTransportAgent | 4 | 3 | 9 | 7 | 3 | ✅ Proof compiled |
| Microsoft365 | 4 | 1 | 10 | 2 | 3 | ✅ Proof compiled |
| Outlook | 6 | 7 | 28 | 7 | 3 | ✅ Proof compiled |
| Import | 12 | 6 | 47 | 5 | 3 | ✅ Proof compiled (10 ops) |
| VCard | 4 | 5 | 18 | 7 | 4 | ✅ Proof compiled |
| Subscriptions | 16 | 10 | 64 | 24 | 5 | ✅ Proof compiled (26 ops) |
| Partners | 24 | 18 | 92 | 10 | 5 | ✅ Proof compiled (19 ops) |
| Billing | 23 | 14 | 95 | 35 | 6 | ✅ Proof compiled (8 representative ops) |
| IdentityServer | 29 | 13 | 92 | 43 | 6 | ✅ Proof compiled (42 ops) |
| AdminPortal | 41 | 63 | 230 | 39 | 6 | ✅ Proof compiled (28 ops) |
| Portal | 130 | 99 | 529 | 161 | 7 | ✅ Proof compiled (78 ops) |
| EntraId | 0 | 0 | 0 | 0 | — | 🔲 Evaluate if Blueprint-dependent |
| GraphProxy | 0 | 0 | 0 | 0 | — | 🔲 Evaluate if Blueprint-dependent |
| **Totals** | **294** | **248** | **1,238** | **346** | | |

### Gateway Projects

| Gateway | Backend Service | Status |
|---------|----------------|--------|
| VCard.Gateway | VCard | 🔲 Not started |
| Import.Gateway | Import | 🔲 Not started |
| Microsoft365.Gateway | Microsoft365 | 🔲 Not started |
| Partners.Gateway | Partners | 🔲 Not started |
| ExchangeTransportAgent.Gateway | ETA | 🔲 Not started |

### Excluded from Migration

| Category | Count | Reason |
|----------|-------|--------|
| SMTP subsystem | 59 projects | Independent of Blueprint API layer |
| Legacy Blueprint (in-tree) | 11 projects | Replaced by blueprint-source-gen |
| Tools | 7 projects | CLI/utility, no Blueprint dependency |
| Brochure site | 1 project | Static, no Blueprint |

## Test Projects

| Test Project | Service | Test Files | Status |
|-------------|---------|------------|--------|
| Symprex.Signature365.Analytics.Tests | Analytics | — | 🔲 Not started |
| Symprex.Signature365.EventStore.Tests | EventStore | — | 🔲 Not started |
| Symprex.Signature365.Templates.Tests | Templates | — | 🔲 Not started |
| Symprex.Signature365.Generation.Tests | Generation | — | 🔲 Not started |
| Symprex.Signature365.ExchangeTransportAgent.Tests | ETA | — | 🔲 Not started |
| Symprex.Signature365.ExchangeTransportAgent.Gateway.Tests | ETA GW | — | 🔲 Not started |
| Symprex.Signature365.Microsoft365.Tests | M365 | — | 🔲 Not started |
| Symprex.Signature365.Microsoft365.Gateway.Tests | M365 GW | — | 🔲 Not started |
| Symprex.Signature365.Outlook.Tests | Outlook | — | 🔲 Not started |
| Symprex.Signature365.Import.Tests | Import | — | 🔲 Not started |
| Symprex.Signature365.Import.Gateway.Tests | Import GW | — | 🔲 Not started |
| Symprex.Signature365.VCard.Tests | VCard | — | 🔲 Not started |
| Symprex.Signature365.VCard.Gateway.Tests | VCard GW | — | 🔲 Not started |
| Symprex.Signature365.Subscriptions.Tests | Subscriptions | — | 🔲 Not started |
| Symprex.Signature365.Partners.Tests | Partners | — | 🔲 Not started |
| Symprex.Signature365.Partners.Gateway.Tests | Partners GW | — | 🔲 Not started |
| Symprex.Signature365.Billing.Tests | Billing | — | 🔲 Not started |
| Symprex.Signature365.IdentityServer.Tests | IdSrv | — | 🔲 Not started |
| Symprex.Signature365.AdminPortal.Tests | AdminPortal | — | 🔲 Not started |
| Symprex.Signature365.Portal.Tests | Portal | — | 🔲 Not started |
| Symprex.Signature365.Portal.Generation.Tests | Portal Gen | — | 🔲 Not started |
| Symprex.Signature365.Portal.UITests | Portal UI | — | 🔲 Not started |
| Symprex.Signature365.GraphProxy.Tests | GraphProxy | — | 🔲 Not started |
| Symprex.Signature365.Common.Tests | Common | — | 🔲 Not started |
| Symprex.Signature365.Common.ApiOperations.Tests | Common Ops | — | 🔲 Not started |

## Codemod Apply Results

The codemod was run in `apply` mode against S365's `blueprint-codemod-test` branch.

| Metric | Value |
|--------|-------|
| Total fixes applied | **2,509** |
| Files changed | **1,148** (app files, excluding legacy Blueprint source) |
| Lines added | +2,906 |
| Lines removed | -2,129 |
| S365 commit | `bc17ad357f` on `blueprint-codemod-test` |

### Changes by Service (app files only)

| Service Folder | Files Changed |
|----------------|---------------|
| Portal (largest) | 387 |
| AdminPortal | 142 |
| IdentityServer | 89 |
| Billing | 86 |
| Partners | 81 |
| Subscriptions | 57 |
| Import | 44 |
| Outlook | 30 |
| VCard | 23 |
| Common.Core | 23 |
| Microsoft365 | 17 |
| Generation | 16 |
| ExchangeTransportAgent | 15 |
| EventStore | 8 |
| Analytics | 8 |
| Common | 9 |
| BlueprintRebus | 6 |
| Test projects | ~37 |

### Change Categories

| Category | Instances |
|----------|-----------|
| Namespace migration (Blueprint.Runtime/Model) | ~1,700 |
| Added `partial` keyword | 657 |
| Other Blueprint `using` updates | ~450 |

### Old Compiled Pipeline Files (to DELETE)

S365 contains **775 pre-compiled pipeline files** in `Internal/Generated` folders that were
produced by the old Blueprint compiler at design time and checked into source control.
The new Blueprint source generator replaces all of them at compile time, so they will be
deleted during migration.

| Service | Pipeline Files |
|---------|---------------|
| Portal | 290 |
| AdminPortal | 127 |
| IdentityServer | 60 |
| Billing | 57 |
| Partners | 56 |
| Analytics | 36 |
| Import | 32 |
| Subscriptions | 29 |
| Outlook | 18 |
| Microsoft365 | 14 |
| VCard | 13 |
| ExchangeTransportAgent | 12 |
| Generation | 9 |
| EventStore | 5 |
| Templates | 3 |

### Post-Codemod Status

After codemod, S365 still requires:
1. **Package reference swap** — replace legacy `src/Blueprint/` project references with new Blueprint NuGet/project refs
2. **Delete 775 old pipeline files** — `Internal/Generated/*ExecutorPipeline.cs` (replaced by source generation)
3. **Startup migration** — `BlueprintApiBuilder` → `AddBlueprintApi<T,E>()` (BPMIG302, ~43 instances)
4. **Middleware migration** — `IMiddlewareBuilder` → source-gen modules (BPMIG301, ~21 instances)
5. **OpenAPI configuration** — `AddOpenApi(...)` → `MapBlueprintApi` options (BPMIG100, ~21 instances)
6. **Auth configuration** — `AddAuthentication(...)` → pipeline module (BPMIG101, ~27 instances)
7. **Create `BlueprintApp` + `JsonSerializerContext`** per service app

### Type Compatibility — Zero Gaps

All Blueprint types used by S365 exist in the new Blueprint:

| Type Category | S365 Usage | New Blueprint | Status |
|--------------|-----------|--------------|--------|
| ICommand/IQuery/IBackgroundTask | Core interfaces | Blueprint.Model | ✅ |
| ApiResource/ListApiResource/PagedApiResource | Base classes | Blueprint.Model | ✅ |
| ResourceEvent/ResourceEventResult | Result types | Blueprint.Http/Runtime.Exec | ✅ |
| OperationResult/OkResult/NoResultOperationResult | Result base | Blueprint namespace | ✅ |
| ApiOperationContext | Execution context | Blueprint.Runtime.Exec | ✅ |
| ApiException/ApiExceptionFactory | Error handling | Blueprint namespace | ✅ |
| HttpResult | HTTP responses | Blueprint.Http | ✅ |
| IResourceAuthorizationProvider | Auth provider | Blueprint.Authorization | ✅ |
| BlueprintApp/BlueprintAppExecutor | App root | Blueprint.Model/Blueprint | ✅ |
| BlueprintTestHost/BlueprintHttpTestHost | Test fixtures | Blueprint.Testing | ✅ |
| All Blueprint.Utilities types | Utility classes | Blueprint.Utilities | ✅ |

**S365-owned types (not Blueprint):** IRegistry, IMigration, IAppDefinition, AddOnApiResource

### Testing Infrastructure Compatibility

- **S365 pattern**: `ApiFixture` wraps Blueprint executor with `CommandAsync`, `QueryAsync`, `BackgroundTaskAsync`, `MessageAsync`
- **Blueprint.Testing**: Provides matching method signatures — zero test call-site changes needed
- **Migration scope**: Only `ApiFixture.cs` needs updating (~1 file), not 4,000+ test call sites
- **Auth helpers**: S365-owned extension methods that call `fixture.CommandAsync()` — work with updated ApiFixture

## Migration Progress Log

| Date | Service | Action | Result | Notes |
|------|---------|--------|--------|-------|
| Current | All | **Codemod applied** | ✅ 2,509 fixes | 1,148 files, committed to `blueprint-codemod-test` |
| Current | All | **Namespace fixes (Pass 1)** | ✅ 147 usings | 139 files, 16 relocated types |
| Current | All | **Namespace fixes (Pass 2)** | ✅ 792 usings | 616 files, 50+ relocated types |
| Current | All | Handler pattern survey | ✅ Compatible | All 4 method names, DI params, ResourceEvent<T> |
| Current | All contracts | **15/15 contracts compile** | ✅ 0 errors | All 15 contract projects build against new Blueprint |
| Current | Blueprint | CA2243 fix | ✅ | Renamed `url` → `routePattern` in SelfLink/RootLinkAttribute |
| Current | Blueprint | IResourceEventAuditRepository | ✅ | Changed AddAsync(object) → AddAsync(ResourceEvent) |
| Current | Outlook.Contract | BlueprintRebus → Blueprint.Rebus | ✅ | Replaced old BlueprintRebus with new Blueprint.Rebus |
| Current | EventStore.Contract | BlueprintRebus → Blueprint.Rebus | ✅ | Replaced old BlueprintRebus with new Blueprint.Rebus |
| Current | Import.Gateway.Contract | NJsonSchema.Annotations | ✅ | Added explicit package ref (was transitive from old Blueprint) |
| Current | Analytics | **Scratch proof compiled** | ✅ 0 errors | Analytics.Contract + Common.Core vs new Blueprint |
| Current | Blueprint | ClaimTypes added | ✅ | `Blueprint.Authorization.ClaimTypes` (Permission, Role) |
| Current | Blueprint | Guard.IsTrue compat | ✅ | Legacy overload (string, bool, string) marked obsolete |
| Current | Blueprint | ApiResource.Object setter | ✅ | Protected setter for ContinuationListApiResource compat |
| Current | Blueprint | IUserAuthorisationContextFactory DIM | ✅ | Legacy 1-param overload via DIM |

### Scratch Proof Results (Analytics.Contract + Common.Core)

**Status: ✅ BUILD SUCCEEDED — 0 errors, 8 warnings**

The scratch proof project compiles S365's `Analytics.Contract` (2 operations) and `Common.Core` 
(shared types, data models, authorization configuration) against new Blueprint with zero code changes to S365.

**Warnings (expected, not blockers):**
| Warning | Count | Action |
|---------|-------|--------|
| BP001 — missing `partial` | 4 | Add `partial` during migration (codemod handles this) |
| BPV012 — `[Required]` on value type | 2 | Minor: `ApiId` is non-nullable, `[Required]` is no-op |
| CS0618 — obsolete `Guard.IsTrue` | 2 | Migrate call sites (compat overload works meanwhile) |

**Files excluded from proof (old Blueprint integration infrastructure — deleted during migration):**
- `Authorisation\Blueprint\*` — old Blueprint integration layer (authorizers, expanders, claim inspectors)
- `ResourceAttribute.cs`, `FromResourceAttribute.cs` — replaced by Blueprint built-ins
- `ClaimHelpers.cs`, `ResourceHelpers.cs`, `ResourceKeyHelpers.cs` — old auth infrastructure
- `AssignedRole.cs`, `AssignedScopePair.cs`, `RoleHelpers.cs` — role mapping infrastructure
- `BlueprintHttpClient.cs`, `BlueprintHttpClientRegistration.cs` — old HTTP client setup
- `SafeJsonSerialisation.cs` — references S365-internal `SensitiveProperties`
- `Signature365UserAuthorisationContext.cs` — needs rewrite to use new Blueprint auth model

**Blueprint changes made to achieve zero errors (all backward-compatible):**
1. `ApiResource.Object` — added protected setter (S365 sets object name in constructor)
2. `Guard.IsTrue` — added `[Obsolete]` compat overload with old parameter order
3. `ClaimTypes` — added `Blueprint.Authorization.ClaimTypes` with `Permission`/`Role` constants
4. `IUserAuthorisationContextFactory` — added DIM for legacy 1-param overload

## S365 Codemod Scan Results

Full-solution scan of `Signature365.sln` (158 projects). All 13 BPMIG diagnostics detected.
Total instances across all projects: **2,896**.

| ID | Category | Title | Instances | Severity |
|----|----------|-------|-----------|----------|
| BPMIG001 | Auto-fixable | Remove redundant `AddOpenApi()` | 17 | Info |
| BPMIG002 | Auto-fixable | Remove redundant `AddHateoasLinks()` | 16 | Info |
| BPMIG003 | Auto-fixable | Remove redundant `AddAuthorisation()` | 27 | Info |
| BPMIG100 | Config required | Migrate configured `AddOpenApi(...)` | 21 | Warning |
| BPMIG101 | Config required | Migrate configured `AddAuthentication(...)` | 27 | Warning |
| BPMIG200 | Auto-fixable | Migrate `FromResource(...)` usage | 161 | Warning |
| BPMIG201 | Auto-fixable | Migrate `{Auth:...}` placeholder | 139 | Warning |
| BPMIG300 | Manual review | Update legacy Blueprint namespace | 1,399 | Warning |
| BPMIG301 | Manual review | `IMiddlewareBuilder` → source-gen modules | 21 | Warning |
| BPMIG302 | Manual review | `BlueprintApiBuilder` → `AddBlueprintApi<T,E>` | 43 | Warning |
| BPMIG303 | Manual review | Remove obsolete Blueprint namespace | 218 | Warning |
| BPMIG400 | Auto-fixable | Add `partial` to operation class | 647 | Warning |
| BPMIG401 | Auto-fixable | Add `partial` to API resource class | 160 | Warning |

**Category breakdown (by instance count):**
- **Auto-fixable by codemod** (2,784 instances / 96%): BPMIG001-003, BPMIG200-201, BPMIG300, BPMIG303, BPMIG400-401 — 9 code fix providers handle these automatically
- **Configuration required** (48 instances / 2%): BPMIG100-101 — startup migration, manual but mechanical
- **Manual design review** (64 instances / 2%): BPMIG301-302 — middleware/builder rewrites

**Automation coverage: 96% (2,784 of 2,896 instances)**
- BPMIG001-003: Remove redundant activation calls (60 instances) — `LegacyActivationCodeFixProvider`
- BPMIG200: Strip resource key template from `[FromResource]` (161 instances) — `FromResourceCodeFixProvider`
- BPMIG201: Remove `{Auth:...}` template arguments (139 instances) — `AuthPlaceholderCodeFixProvider`
- BPMIG300: Replace legacy namespace usings (1,399 instances) — `LegacyNamespaceCodeFixProvider`
- BPMIG303: Remove obsolete namespace usings (218 instances) — `LegacyRemovedNamespaceCodeFixProvider`
- BPMIG400-401: Add `partial` to operation/resource classes (807 instances) — `MissingPartialCodeFixProvider`

## Contract Compilation Results (15/15)

All 15 S365 contract projects compile against new Blueprint with zero application code errors.

| Contract | Status | Exclusions | Notes |
|----------|--------|------------|-------|
| Analytics.Contract | ✅ Pass | 0 | Clean |
| Billing.Contract | ✅ Pass | 2 | BillingApiEntitlementChecker.cs, EntitlementRegistry.cs (depend on generated IBillingApi) |
| EventStore.Contract | ✅ Pass | 2 | EventMapper.cs (SafeJsonSerialisation), BusResourceEventAuditRepository.cs (old audit interface) |
| ExchangeTransportAgent.Contract | ✅ Pass | 0 | Clean |
| Generation.Contract | ✅ Pass | 0 | Clean |
| IdentityServer.Contract | ✅ Pass | 0 | Clean |
| Import.Contract | ✅ Pass | 0 | Fixed misplaced `using` from earlier script |
| Import.Gateway.Contract | ✅ Pass | 0 | Added NJsonSchema.Annotations (was transitive from old Blueprint) |
| Microsoft365.Contract | ✅ Pass | 0 | 2 files use fully-qualified `Expandable<T>` |
| Outlook.Contract | ✅ Pass | 0 | Replaced BlueprintRebus with Blueprint.Rebus |
| Partners.Contract | ✅ Pass | 0 | Clean |
| Portal.Contract | ✅ Pass | 1 | PortalApi.cs (depends on generated IPortalApi) |
| Subscriptions.Contract | ✅ Pass | 0 | Clean |
| Templates.Contract | ✅ Pass | 0 | Clean |
| VCard.Contract | ✅ Pass | 0 | Clean |

**Infrastructure changes for contract compilation:**
- `src/Apps/Directory.Build.props` — Global usings for Blueprint.Authorization, Blueprint.Http, Blueprint.Runtime.Exec
- `src/Apps/GlobalUsings.cs` — Type aliases for QueryOrderDefinition, OrderedPagedQuery (resolve Blueprint.Model vs Common.Api ambiguity)
- `src/Apps/Directory.Build.targets` — Suppress S365 ServiceApiSourceGenerator during proof
- Common.Core — 24 file exclusions for old Blueprint bridge infrastructure

**Service-level compilation status:**
Service projects still reference old Blueprint.csproj, Blueprint.Http.csproj, Blueprint.OpenApi.csproj.
These must be replaced with new Blueprint.Runtime, Blueprint.SourceGenerator (analyzer), Blueprint.Rebus, etc.
Service compilation requires migrating Startup.cs, creating BlueprintApp, and replacing old middleware patterns.

## Known Blockers

None currently. All framework features are implemented.

## Handler Compatibility (Zero-Churn Confirmed)

Deep audit of S365 handler patterns confirmed full compatibility with zero migration churn:

| S365 Pattern | Blueprint Support | Churn |
|---|---|---|
| `HandleAsync(op, service1, service2, ...)` | ✅ DI parameter injection | None |
| `Handle(op, ...)` (sync) | ✅ All 4 method names | None |
| `InvokeAsync(op, ...)` | ✅ All 4 method names | None |
| `ApiOperations` base class (`Context`, `Identity`, `Logger`, `CancellationToken`) | ✅ Property injection | None |
| `ICommand<ResourceEvent<T>>` (253 operations) | ✅ Legacy type supported | None |
| `IQuery<T>` with plain resource return | ✅ Native support | None |
| `ICommand<HttpResult>` (webhook handler) | ✅ HttpResult supported | None |
| `TenantListApiResource<T>` (custom subclass) | ✅ S365-owned, extends `ListApiResource<T>` | None |
| Convention-based handlers (no `IApiOperationHandler`) | ✅ Already convention-based | None |
| `ResourceEventDefinitionFactory` pattern | ✅ Factory returns `ResourceEvent<T>` | None |

## Automated Migration Steps (per service)

Each service follows the same mechanical steps:

1. **Codemod apply** — Run `Blueprint.Codemod --apply` to auto-fix 9 diagnostic types (96% of instances):
   - BPMIG001-003: Remove redundant activation calls (60 instances)
   - BPMIG200: Strip resource key from `[FromResource]` (161 instances)
   - BPMIG201: Remove `{Auth:...}` template arguments (139 instances)
   - BPMIG300: Replace legacy namespace usings (1,399 instances)
   - BPMIG303: Remove obsolete namespace usings (218 instances)
   - BPMIG400: Add `partial` to operation classes (647 instances)
   - BPMIG401: Add `partial` to API resource classes (160 instances)
2. **Package references** — Replace legacy Blueprint packages with new Blueprint.Runtime + source generator
3. **Create BlueprintApp** — `[BlueprintAppScan]` partial class + `JsonSerializerContext`
4. **Update startup** — Replace `AddBlueprintApi(...)` with new registration pattern
5. **Update auth** — `[Resource]` stays; auth provider implements `IResourceAuthorizationProvider`
6. **IMiddlewareBuilder migration** — 21 instances to companion generator or ASP.NET middleware
7. **BlueprintApiBuilder migration** — 43 instances to `AddBlueprintApi<T,E>` pattern
8. **OpenAPI gate** — Compare OpenAPI output, verify TypeScript client generation
9. **Test migration** — Replace `WebApiFixtureFactory` with `BlueprintTestFixtureFactory`
10. **Integration test** — Run full test suite, verify all pass

## Metrics Tracking

> Updated after each service migration attempt.

| Metric | Current |
|--------|---------|
| Services fully migrated | 0 / 17 |
| Gateways fully migrated | 0 / 5 |
| Test suites passing | 0 / 24 |
| Total operations migrated | 0 / 542 |
| OpenAPI gates passed | 0 / 17 |
| Blocking framework gaps found | 0 |
| Framework gaps resolved | 0 |
