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
| Migration status | **Pre-migration — Blueprint framework ready** |

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

### Post-Codemod Status

After codemod, S365 still requires:
1. **Package reference swap** — replace legacy `src/Blueprint/` project references with new Blueprint NuGet/project refs
2. **Startup migration** — `BlueprintApiBuilder` → `AddBlueprintApi<T,E>()` (BPMIG302, ~43 instances)
3. **Middleware migration** — `IMiddlewareBuilder` → source-gen modules (BPMIG301, ~21 instances)
4. **OpenAPI configuration** — `AddOpenApi(...)` → `MapBlueprintApi` options (BPMIG100, ~21 instances)
5. **Auth configuration** — `AddAuthentication(...)` → pipeline module (BPMIG101, ~27 instances)

## Migration Progress Log

| Date | Service | Action | Result | Notes |
|------|---------|--------|--------|-------|
| Current | All | **Codemod applied** | ✅ 2,509 fixes | 1,148 files, committed to `blueprint-codemod-test` |
| Current | All | Handler pattern survey | ✅ Compatible | All 4 method names, DI params, ApiOperations base class, ResourceEvent<T> return types |

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
