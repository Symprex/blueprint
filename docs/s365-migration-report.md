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

## Migration Progress Log

| Date | Service | Action | Result | Notes |
|------|---------|--------|--------|-------|
| — | — | — | — | Migration not yet started |

## S365 Codemod Scan Results

Full-solution scan of `Signature365.sln` (158 projects). All 13 BPMIG diagnostics detected:

| ID | Category | Title | Severity |
|----|----------|-------|----------|
| BPMIG001 | Auto-fixable | Remove redundant `AddOpenApi()` | Info |
| BPMIG002 | Auto-fixable | Remove redundant `AddHateoasLinks()` | Info |
| BPMIG003 | Auto-fixable | Remove redundant `AddAuthorisation()` | Info |
| BPMIG100 | Configuration required | Migrate configured `AddOpenApi(...)` | Warning |
| BPMIG101 | Configuration required | Migrate configured `AddAuthentication(...)` | Warning |
| BPMIG200 | Consumer-extension rewrite | Migrate `FromResource(...)` usage | Warning |
| BPMIG201 | Consumer-extension rewrite | Migrate `{Auth:...}` placeholder | Warning |
| BPMIG300 | Manual design review | Update legacy Blueprint namespace | Warning |
| BPMIG301 | Manual design review | `IMiddlewareBuilder` → source-gen modules | Warning |
| BPMIG302 | Manual design review | `BlueprintApiBuilder` → `AddBlueprintApi<T,E>` | Warning |
| BPMIG303 | Manual design review | Remove obsolete Blueprint namespace | Warning |
| BPMIG400 | Auto-fixable | Add `partial` to operation class | Warning |
| BPMIG401 | Auto-fixable | Add `partial` to API resource class | Warning |

**Category breakdown:**
- **Auto-fixable** (5): BPMIG001-003, BPMIG400-401 — code fixes handle these automatically
- **Configuration required** (2): BPMIG100-101 — startup migration, manual but mechanical
- **Consumer-extension rewrite** (2): BPMIG200-201 — `[FromResource]` and `{Auth:...}` patterns
- **Manual design review** (4): BPMIG300-303 — namespace changes, middleware/builder rewrites

## Known Blockers

None currently. All framework features are implemented.

## Automated Migration Steps (per service)

Each service follows the same mechanical steps:

1. **Codemod scan** — Run `Blueprint.Codemod` against the service to generate SARIF diagnostics
2. **Package references** — Replace legacy Blueprint packages with new Blueprint.Runtime + source generator
3. **Add `partial` keyword** — All operation classes must be `partial`
4. **Create BlueprintApp** — `[BlueprintAppScan]` partial class + `JsonSerializerContext`
5. **Update startup** — Replace `AddBlueprintApi(...)` with new registration pattern
6. **Update auth** — `[Resource]` stays; auth provider implements `IResourceAuthorizationProvider`
7. **Update result types** — `ResourceEvent<T>` already compatible; review other return types
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
