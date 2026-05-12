# Signature 365 → Blueprint Source-Gen: Migration Findings

> Living document tracking migration progress, problems, and open questions.
> Updated by AI agent sessions as migration work proceeds.

## Status Summary

| Area | Status | Notes |
|------|--------|-------|
| Compile-time proof | ✅ Done | `.scratch/s365-proof/` compiles, 46 generated files |
| Codemod tool | ✅ Done | Per-project SARIF, lenient build failure handling |
| .NET 10 upgrade | ✅ Done | All projects target net10.0 |
| Blueprint.Testing | ✅ Done | BlueprintTestFixture with ambient auth, QueryAsync/CommandAsync |
| Blueprint.Marten | ✅ Done | IUnitOfWork, outbox, multi-tenancy session scoping |
| Blueprint.Rebus | ✅ Done | Companion generator, transaction scope |
| Blueprint.Tasks.Hangfire | ✅ Done | Schedule provider, recurring tasks |
| `[Resource]` built-in | ✅ Done | Noun/verb with FromResource template override |
| First-class tenancy | ✅ Done | TenantModule in pipeline, claim-based resolution |
| Pipeline body extraction | ✅ Done | `PipelineHelpers.ReadJsonBodyAsync` (~80 lines → 1 line) |
| Minimize test churn | ✅ Done | BlueprintTestFixture with ambient auth, extension methods |
| OpenAPI well-known endpoint | ✅ Done | RFC 9727 `/.well-known/openapi` |
| Compile-time populators | ✅ Done | PropertyOwnershipModule + TenantModule handle all at compile time |
| TypeScript generation compat | ✅ Documented | Custom Handlebars templates analysed (see below) |
| Codemod S365 run | ✅ Done | 10 BPMIG diagnostics found (3 auto-fixable, 2 config, 2 rewrite, 3 manual) |
| Load tests (NBomber) | ✅ Done | 9 scenarios: GET/POST/PUT/DELETE, validation, auth, OpenAPI, health |
| Runtime benchmarks | ✅ Done | 7 benchmark classes: pipeline, validation, population, scaling, compilation |
| Profiling guide | ✅ Done | `docs/performance-profiling.md` (308 lines) |
| ReadJsonBodyAsync static lambda | ✅ Done | Generic delegate eliminates closure allocation per request |
| Route deduplication | ✅ Done | `HashSet<(Route, Type)>` dedup in endpoint registration |
| StateBasedLinkChecker | ✅ Done | Identical API to S365, sample + integration tests |
| Gateway sample + HTTP client E2E | ✅ Done | `Blueprint.SourceGenerator.Sample.Gateway` with 6 E2E tests |
| Permission removal | ✅ N/A | Codebase only has `[Resource]`-based auth — no Permission attrs exist |

---

## Confirmed Facts

### Serialization
- **S365 uses System.Text.Json (STJ) for API responses**, not Newtonsoft.Json
- Newtonsoft is only used for Marten persistence serialization (JSONB storage)
- Blueprint source-gen's STJ-first approach is fully compatible
- S365 will need `JsonSerializerContext` per service for AOT-friendly serialization
- NodaTime types are used extensively; `NodaTime.Serialization.SystemTextJson` is available

### Authorization Model
- S365 exclusively uses `[Resource(Noun, Verb)]` — no `[Permission]`/`[PermissionFor]`
- `ScopeApiAuthoriser` checks JWT `scope` claims against expected `{Noun}.{Verb}` pairs
- `AccountId = TenantId ?? PartnerId ?? "Symprex"` — tenant-first hierarchy
- `IS365ResourceKeyExpander` does hierarchical resource key expansion via Marten queries
- Ignored scopes: `openid`, `profile`, `Signature365.ReadWrite.All`, `Signature365Admin.ReadWrite.All`

### Multi-Tenancy
- `ICurrentTenantAccessor.Current` is set by ASP.NET Core middleware before Blueprint pipeline
- `IDocumentSession` is scoped: `store.IdentitySession(tenantId)` when tenant is present
- Tenant ID comes from JWT claim `sig365_tenant_id`

### Messaging (Rebus)
- `UseMartenOutbox()` stores outgoing messages in Marten outbox table
- `StartRebusTransactionScopeMiddleware` wraps pipeline with `RebusTransactionScope`
- `CommitRebusTransactionScopeMiddleware` calls `CompleteAsync()` after handler
- `[NoRebusTransaction]` attribute opts out specific handlers
- `[DoNotSaveChanges]` opts out of `SaveMartenChangesMiddleware`

### TypeScript Generation
- **NOT NSwag** — S365 uses custom Handlebars templates in 3 applications
- Templates load OpenAPI JSON from disk and generate `api.ts`
- OpenAPI shape must be compatible with existing templates
- **Template locations** (identical structure in each app):
  - `frontend/apps/portal/src/openapi/generator/`
  - `frontend/apps/admin/src/openapi/generator/`
  - `frontend/apps/partners/src/openapi/generator/`
- **4 template files per app**: `api.handlebars`, `apiInner.handlebars`, `index.handlebars`, `model.handlebars`
- **OpenAPI spec loaded from disk**: e.g. `src/Apps/Portal/.../Spec/openapi.json`
- **Script**: `npm run openapi` (or `pnpm run openapi`) in each frontend app
- **Generator** (`index.mts`): Builds `TemplateContext` with grouped operations, schemas, return types
- **Custom Handlebars helpers**: `pathGenerator` (URL interpolation), `ajvSchema`, `requiresRtti`, `withOperation`
- **Output**: `api.ts` with schema registry, typed model classes (with `setData`/`typeDetails`/`schema`), and per-tag API classes
- **x-links**: Templates use OpenAPI `x-links` extension for resource method generation on model classes
- **Migration impact**: OpenAPI schema property names (`operationId`, paths, response schemas) must match current shape

### SMTP Subsystem
- 59 projects, does NOT use Blueprint — only an HTTP client
- **Excluded from migration scope**

---

## S365 Scale (Verified)

| Metric | Count |
|--------|-------|
| Total projects | ~158 |
| Blueprint-consuming services | 16+ |
| ICommand implementations | 342 |
| IQuery implementations | 290 |
| Total operations | 632 |
| `[Resource(...)]` usages | 349 |
| `[FromResource(...)]` (legacy) | 158 |
| S365Nouns constants | 96 |
| Gateway projects | 5 |
| Contract assemblies | ~14 |
| Test projects | 42 |
| SMTP subsystem (excluded) | 59 |

---

## Codemod Results (S365 Analysis)

Successfully ran Blueprint codemod against `Signature365.sln` (158 projects). Found **10 BPMIG diagnostics** across 4 categories:

### Auto-fixable (3)
| ID | Title | Action |
|----|-------|--------|
| BPMIG001 | Remove redundant `AddOpenApi()` | Delete call — OpenAPI is implied by HTTP host |
| BPMIG002 | Remove redundant `AddHateoasLinks()` | Delete call — links are inferred from attributes |
| BPMIG003 | Remove redundant `AddAuthorisation()` | Delete call — auth is inferred from `[Resource]` |

### Configuration Required (2)
| ID | Title | Action |
|----|-------|--------|
| BPMIG100 | Migrate configured `AddOpenApi(...)` | Move customization to `configureOpenApi` in `MapBlueprintApi` |
| BPMIG101 | Migrate configured `AddAuthentication(...)` | Move context loader to new auth configuration seam |

### Consumer-Extension Rewrite (2)
| ID | Title | Action |
|----|-------|--------|
| BPMIG200 | Migrate `FromResource(...)` | Use new `[FromResource(typeof(X))]` with optional template override |
| BPMIG201 | Migrate `{Auth:...}` placeholders | Handled by `ResourceKeyTemplateExpander` — verify template format |

### Manual Design Review (3)
| ID | Title | Action |
|----|-------|--------|
| BPMIG300 | Update legacy namespaces | `using Blueprint.Api` → `using Blueprint.Model` etc. |
| BPMIG301 | IMiddlewareBuilder → modules | Custom middleware → companion generator or ASP.NET middleware |
| BPMIG302 | BlueprintApiBuilder → AddBlueprintApi | Complete startup rewrite per service |

---

## Problems Encountered

### 1. Codemod SARIF Concatenation (Fixed)
- **Problem**: `/p:ErrorLog=path.sarif` applied globally caused all 158 projects to write to same file
- **Fix**: Per-project SARIF via `.targets` file with `$(MSBuildProjectName)` expansion
- **Commit**: `851b7c2`

### 2. Codemod Build Failure on Pre-existing Warnings (Fixed)
- **Problem**: Codemod threw when S365 build had non-migration diagnostics (CA*, SA*, CS*) even when BPMIG diagnostics were found
- **Fix**: Only throw when build fails AND zero BPMIG diagnostics found; report BPMIG findings regardless of other warnings
- **Impact**: Unlocked codemod analysis for S365 — 10 BPMIG diagnostics successfully detected

### 2. WithTenant Regression (Fixed)
- **Problem**: Adding `WithScopePairs` accidentally removed `WithTenant` methods
- **Fix**: Restored methods alongside new helpers
- **Commit**: `fa03a48`

### 3. HasConcreteProperty False Positive (Fixed, Prior Session)
- **Problem**: Source generator included inherited `Object` properties (ToString, etc.)
- **Fix**: Filter to only properties declared on the concrete type
- **Impact**: Blocked S365 proof project compilation

---

## Open Questions

1. ~~**OpenAPI endpoint path**: What is the current best practice?~~ → **Resolved**: RFC 9727 `/.well-known/openapi`
2. ~~**Pipeline IL size**: How much duplication exists?~~ → **Resolved**: ~80 lines per pipeline → 1 line via `ReadJsonBodyAsync`
3. ~~**Tenant middleware ordering**: Should tenant resolution be ASP.NET middleware or pipeline stage?~~ → **Resolved**: TenantModule in pipeline
4. ~~**Property populator elimination**: Can ALL S365 property populators become compile-time?~~ → **Resolved**: Yes, PropertyOwnershipModule + TenantModule
5. ~~**Resource key template**: How should `{Auth:AccountId}` expansion integrate with generated code?~~ → **Resolved**: `FromResource` template override
6. **Handlebars template compatibility**: What specific OpenAPI shape properties do the templates expect?
   - Need to compare `operationId` naming between legacy and new Blueprint
   - Templates use `x-links` extension — Blueprint must emit matching extension data
   - `pathGenerator` helper interpolates route params — path format must be `{paramName}` style
7. **StateBasedLinkChecker**: S365 uses state-based link checkers for conditional HATEOAS links — need to verify Blueprint supports this pattern
8. **InvokeAsync convention**: S365 operations can have `InvokeAsync()` directly — Blueprint source-gen expects separate handlers

---

## S365 Test Infrastructure Mapping

S365 testing uses `WebApiFixtureFactory<TStartup>` → `ApiFixture` with:

| S365 Pattern | Blueprint.Testing Equivalent | Notes |
|---|---|---|
| `WebApiFixtureFactory<TStartup>` | `BlueprintTestHostBuilder` | Builder pattern, `WithTestDefaults()` |
| `apiFactory.Create()` | `builder.BuildFixture()` | Returns `BlueprintTestFixture` |
| `fixture.CommandAsync<T>(cmd)` | `fixture.CommandAsync<T>(cmd)` | ✅ Direct match |
| `fixture.QueryAsync<T>(query)` | `fixture.QueryAsync<T>(query)` | ✅ Direct match |
| `fixture.BackgroundTaskAsync(task)` | `fixture.ExecuteAsync(task)` | Different name but same concept |
| `fixture.MessageAsync(msg)` | `fixture.ExecuteAsync(msg)` | Different name but same concept |
| `fixture.Authentication.Set(identity)` | `fixture.Authentication.SetIdentity(identity)` | ✅ Ambient auth supported |
| `AuthenticateAsTenantAdministrator()` | Extension method needed | S365-specific, keep in S365 test helpers |
| `fixture.Bus` (FakeBus) | `fixture.Messages` (TestMessageCollector) | Different assertion API |
| `Bus.Should().HavePublishedSingle<T>()` | Need fluent assertion extensions | S365-specific FakeBus assertions |
| `fixture.Tasks` | `fixture.Tasks` (BackgroundTaskCollector) | ✅ Direct match |
| `apiFactory.Create(s => s.AddSingleton(...))` | `builder.Configure(s => ...)` then `BuildFixture()` | Per-test DI overrides |
| `IntegrationTestDatabase` | External to Blueprint | S365-specific DB management |
| `fixture.WithDatabaseAsync(tenant, ...)` | External extension method | Marten-specific, keep in S365 |

**Key decisions to minimize S365 test churn:**
- `BlueprintTestFixture` already has `CommandAsync`/`QueryAsync` matching S365's `ApiFixture`
- Ambient auth via `fixture.Authentication` matches S365's `Authentication` class pattern
- `BackgroundTaskAsync` and `MessageAsync` could be added as convenience aliases
- FakeBus fluent assertions are S365-specific — keep as extension methods in S365 test helpers
- `WebApiFixtureFactory<T>` could be provided as a thin S365-side wrapper that creates `BlueprintTestHostBuilder`

---

## S365 Operation Patterns

### Return Type Mapping

| S365 Return Type | Blueprint Return Type | Notes |
|---|---|---|
| `ResourceEvent<T>` | `ResourceEventResult<T>` | Name change only |
| `StatusCodeResult.Accepted` | `AcceptedResult` / `NoResultOperationResult` | Need to verify |
| `StatusCodeResult.NoContent` | `HttpNoContentResult` | Already supported |
| `ListApiResource<T>` | `ListApiResource<T>` | ✅ Same |
| Plain `T` | `OkResult<T>` or plain `T` | Blueprint supports both |

### Execution Patterns

| Pattern | S365 Usage | Blueprint Support |
|---|---|---|
| Separate `IApiOperationHandler<T>` | Primary pattern | ✅ Supported via handler discovery |
| `InvokeAsync()` on operation class | Secondary pattern (e.g. `DeleteBusQueueMessagesCommand`) | ✅ Supported via convention |
| Multi-operation handlers | Occasional | Need to verify |
| `StateBasedLinkChecker<TOp, TResource>` | Conditional HATEOAS links | Need to verify/implement |

---

## Session Log

| Date | Session | Key Changes |
|------|---------|-------------|
| 2025-07 | Initial | S365 proof project, codemod tool, .NET 10 upgrade, testing helpers |
| 2025-07 | Session 2 | Resource auth builtin, tenant first-class, migration findings doc |
| 2025-07 | Current | BlueprintTestFixture, FromResource override, well-known OpenAPI, body reading extraction, S365 deep analysis (632 ops, test infra mapping, TypeScript gen) |
