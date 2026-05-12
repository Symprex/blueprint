# Signature 365 → Blueprint Source-Gen: Migration Findings

> Living document tracking migration progress, problems, and open questions.
> Updated by AI agent sessions as migration work proceeds.

## Status Summary

| Area | Status | Notes |
|------|--------|-------|
| Compile-time proof | ✅ Done | `.scratch/s365-proof/` compiles, 46 generated files |
| Codemod tool | ✅ Done | Per-project SARIF, lenient build failure handling |
| .NET 10 upgrade | ✅ Done | All projects target net10.0 |
| Blueprint.Testing | ✅ Done | BlueprintTestFixture with ambient auth, S365 compat shims |
| Blueprint.Marten | ✅ Done | IUnitOfWork, outbox, multi-tenancy session scoping |
| Blueprint.Rebus | ✅ Done | Companion generator, transaction scope |
| Blueprint.Tasks.Hangfire | ✅ Done | Schedule provider, recurring tasks |
| `[Resource]` built-in | ✅ Done | Noun/verb with FromResource template override |
| First-class tenancy | ✅ Done | TenantModule in pipeline, claim-based resolution |
| Pipeline body extraction | ✅ Done | `PipelineHelpers.ReadJsonBodyAsync` (~80 lines → 1 line) |
| Minimize test churn | ✅ Done | Typed overloads, anonymous BackgroundTask/Message, factory pattern |
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
| Test fixture factory | ✅ Done | `BlueprintTestFixtureFactory` mirrors S365 `WebApiFixtureFactory` |
| Test auth S365 compat | ✅ Done | `Set(ClaimsIdentity)`, `As(identity)`, `MakeAnonymous()` API match |
| `[Link]` attribute support | ✅ Done | Source gen processes runtime `LinkAttribute` (292 S365 usages) |
| HttpHostExposure centralisation | ✅ Done | 13 modules refactored to shared `HttpHostExposureHelper` |
| OpenAPI request body `$ref` | ✅ Done | Request body schemas now use `$ref` to component schemas |
| `[FromResource]` compatibility | ✅ Verified | S365 2-arg form fully compatible, compile-time traversal |
| PropertyOwnership route extraction | ✅ Done | Now includes `LinkAttribute` routes via shared helper |
| Handler discovery compat | ✅ Verified | InvokeAsync/Handle/HandleAsync all recognised |
| ResourceEvent compat | ✅ Verified | Identical interface between S365 and Blueprint |
| IFormFile / multipart | ✅ Not needed | No S365 operations use IFormFile (only SMTP, excluded) |
| IPropertyPopulator | ✅ Not needed | S365 has none; TenantModule handles TenantId at compile time |
| Claims-based auth removal | ✅ Done | Removed 9 files, 775 lines; only Resource-based auth remains |
| `RequiresPermissionCheck` rename | ✅ Done | → `RequiresResourceAuthorization` for clarity |
| S365 auth compatibility | ✅ Verified | 3-arg `[Resource]` matches S365 pattern; verb expansion stays in provider |
| Pipeline helper extraction | ✅ Done | `LoadUserContextAsync`, `RunAuthorisersAsync` reduce IL per pipeline |
| Handler property injection | ✅ Done | ApiOperations base-class pattern (Context, Identity, Logger, CT) auto-detected |
| Self-link dedup in pipelines | ✅ Done | Multiple ops declaring same `[SelfLink]` URL no longer emit duplicate `AddLink("self")` calls |
| ResourceEventAudit extraction | ✅ Done | ~15 lines inline per pipeline → `PipelineHelpers.ExecuteResourceEventAuditSinkAsync` (saves ~4,110 lines for S365's ~274 ResourceEvent ops) |
| ScopePair/ResolvedScopePair structs | ✅ Done | Changed from `sealed record` to `readonly record struct` — eliminates per-element heap allocations in auth paths |
| Constructor service array helper | ✅ Done | `PipelineHelpers.ResolveServiceArray<T>()` replaces ~180-char inline expression across all pipelines |
| GetHttpContext() compat method | ✅ Done | Instance method on ApiOperationContext for S365 HttpContext access |
| StatusCodeResult returns | ✅ Verified | Accepted (85 S365 usages), NoContent (10), Created (12) all work |
| `[DoNotSaveChanges]` | ✅ Verified | Already supported with BPP001 diagnostic for misuse on non-commands |
| Task (void) handler returns | ✅ Verified | 65 S365 Rebus handlers returning Task → NoResult normalization |
| S365 body reading pattern | ✅ Verified | Operations-as-body pattern matches Blueprint exactly; 160 POST/PUT ops |
| Custom JsonConverters | ✅ S365-owned | `ObjectValueJsonConverter`, `DictionaryStringObjectJsonConverter` are S365-specific; registered in S365 DI |
| QueryAsync/CommandAsync shims | ✅ Done | Already on BlueprintTestHost and BlueprintTestFixture |
| S365 TenantId type on operations | ✅ Compatible | HTTP ops use `string TenantId` (TenantModule handles); `ApiId` only on domain events |
| S365 ICurrentTenantAccessor pattern | ✅ Compatible | AsyncLocal-based, matches Blueprint ITenantAccessor exactly |
| S365 Marten tenant isolation | ✅ S365-owned | `TenancyStyle.Conjoined` via IHaveTenantId; S365 creates tenant-scoped session in DI registration |
| S365 SetTenantMiddleware → TenantModule | ✅ Compatible | SetTenantMiddleware sets accessor + populates TenantId property — TenantModule does both |
| S365 partner vs tenant distinction | ✅ S365-owned | PartnerId claim handling stays in S365's ITenantResolver implementation |
| InvokeAsync inline handlers | ✅ Verified | HandlerDiscovery supports Invoke/InvokeAsync/Handle/HandleAsync on operation class |
| x-links OpenAPI extension | ✅ Verified | Blueprint emits x-links with rel/operationId/method; matches S365 Handlebars templates |
| Pipeline helper: UoW/Outbox/Tasks | ✅ Done | `SaveUnitOfWorkAsync`, `CommitMessageOutboxAsync`, `FlushBackgroundTasksAsync` extracted |
| Singleton result types | ✅ Done | `ForbiddenResult.Instance`, `UnauthenticatedResult.Instance`, `UnsupportedMediaTypeResult.Instance` — zero allocation on auth/content-type failure paths |
| GetServiceAs test double accessor | ✅ Done | `GetServiceAs<TService, TImpl>()` for safe casting to test doubles (e.g. FakeBus) |
| WrapConfigureAnonymous optimization | ✅ Done | Cached static no-op delegate eliminates closure allocation for background task/message tests |

---

## Confirmed Facts

### Serialization
- **S365 uses System.Text.Json (STJ) for API responses**, not Newtonsoft.Json
- Newtonsoft is only used for Marten persistence serialization (JSONB storage)
- Blueprint source-gen's STJ-first approach is fully compatible
- S365 will need `JsonSerializerContext` per service for AOT-friendly serialization
- NodaTime types are used extensively; `NodaTime.Serialization.SystemTextJson` is available

### Authorization Model
- S365 exclusively uses `[Resource(Noun, Verb, ResourceKeyTemplate)]` — all 3 params always provided
- `[Permission]` / `[PermissionFor]` exist in legacy Blueprint but are NOT used in S365 production code
- Blueprint's claims-based auth layer has been removed; only Resource-based auth remains
- `ScopeApiAuthoriser` checks JWT `scope` claims against expected `{Noun}.{Verb}` pairs (legacy path)
- `S365ClaimsApiAuthoriser` is the modern path using role/scope pair-based checks with claim expansion
- `AccountId = TenantId ?? PartnerId ?? "Symprex"` — tenant-first hierarchy
- S365 verb expansion: "Manage" includes Create, Read, Update, Delete — handled by `IResourceAuthorizationProvider`
- `{Auth:AccountId}` and `{Auth:Id}` template tokens are supported by `ResourceKeyTemplateExpander`
- `{PropertyName}` tokens are expanded via compile-time generated property access (no reflection)
- S365 `AuthResourceKeyTemplateExpander` → Blueprint `ResourceKeyTemplateExpander` (static, already works)
- S365 `PropertyResourceKeyTemplateExpander` (reflection-based) → Blueprint compile-time expansion (superior)

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

### Link Attributes
- S365 has **193 `[SelfLink]`** and **292 `[Link]`** usages (99 non-self HATEOAS links)
- Blueprint source-gen now processes all three: `[RootLink]`, `[SelfLink]`, `[Link]`
- `[Link]` requires explicit `Rel` value (BPLG006 diagnostic if missing)
- Null resource type on `[Link]` treated as root-link semantics (URL only, no link registration)
- Index-based variable names (`link_0`, `link_1`) used in generated code for safe C# identifiers
- PropertyOwnershipModule now extracts route placeholders from `[Link]` URLs too
- 13 modules refactored to use centralised `HttpHostExposureHelper` (was duplicate per-module)

### OpenAPI Compatibility
- Blueprint uses `operationId` = operation class name (e.g., `GetTemplateQuery`)
- S365 legacy also uses class name — **compatible**
- Custom Handlebars templates use `operation.operationId` for lookup — works unchanged
- `pathGenerator` helper builds URL interpolation from path — uses standard `{paramName}` format
- Templates group operations by `tags` — Blueprint's OpenAPI groups by namespace, configurable
- **Critical**: `x-links` extension must be present in Blueprint OpenAPI output for model resource methods

### Background Tasks (Hangfire)
- S365 uses `[Queue("portal")]` attribute on background tasks — **already supported**
- `BackgroundTaskEnvelope.GetQueueName()` reads attribute (cached reflection, functional)
- Custom `RecurringJobPartitionedSqlServerStorage` — S365 provides, Blueprint compatible
- `IRecurringTaskScheduler` discovery — S365 configures in startup, not auto-discovered
- No compile-time gaps for background tasks

### Form Body Parsing
- S365 has `FormBodyParser` for multipart/form-data in Blueprint.Http
- **No S365 operations use IFormFile** — only the parser infrastructure references it
- Blueprint can defer multipart support — not blocking migration

### Handler Property Injection (ApiOperations Base Class)
- S365 handlers inherit from `ApiOperations` base class with settable properties:
  - `Context` (`ApiOperationContext`) — pipeline context
  - `Identity` (`IUserAuthorisationContext`) — resolved user context
  - `Logger` (`ILogger`) — pipeline-scoped logger
  - `CancellationToken` — request cancellation token
  - Derived: `HttpContext`, `HttpRequest`, `HttpResponse` (computed from `Context`)
- Legacy Blueprint generates property assignments after handler instantiation
- **New Blueprint source-gen now auto-detects** these properties via `DetectPropertyInjections`
  in `HandlerDiscovery.FindExternalHandler` — walks class hierarchy for settable properties
  matching well-known types
- Generated pipeline emits: `handler.Context = context;`, `handler.Identity = context.UserAuthorisationContext;`, etc.
- `OperationExecutorShell` now exposes `protected ILogger Logger` for generated pipelines to use
- **Impact**: Handlers extending ApiOperations work without code changes — no migration churn

### Pipeline Helper Extraction
- `LoadUserContextAsync` extracted from per-pipeline inline code (~15 lines saved per pipeline)
  - Identity check → factory call → APM activity tags
- `RunAuthorisersAsync` extracted from per-pipeline inline code (~35 lines saved per pipeline)
  - IApiAuthoriser[] + IOperationAuthoriser[] loops with indexed `for` (no enumerator)
  - Returns `OperationResult?` (null = pass, non-null = 401/403 denial)
- Both are `public static` on `PipelineHelpers` for cross-assembly access
- **Impact for S365**: With 632 operations, this saves ~31,600 lines of generated IL

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
6. ~~**Handlebars template compatibility**: What specific OpenAPI shape properties do the templates expect?~~ → **Resolved**: Blueprint emits `x-links` with `rel`, `operationId`, `method` — matches S365 template expectations exactly. `$links` serialized via `[JsonPropertyName("$links")]` on `ApiResource.Links`. `operationId` = class name (same convention). Path format `{paramName}` compatible.
7. ~~**StateBasedLinkChecker**: S365 uses state-based link checkers for conditional HATEOAS links~~ → **Resolved**: Fully implemented with identical API
8. ~~**InvokeAsync convention**: S365 operations can have `InvokeAsync()` directly~~ → **Resolved**: `HandlerDiscovery.FindInlineHandler()` supports `Invoke`, `InvokeAsync`, `Handle`, `HandleAsync` method names
9. ~~**ProxyResult (Portal CubeJS analytics)**: Is this a Blueprint gap?~~ → **Resolved**: S365-specific result type; extends `HttpResult` (already designed for consumer subclasses). S365 defines `ProxyResult` locally.
10. ~~**Exception translation ordering**: Does Blueprint preserve catch clause type hierarchy?~~ → **Resolved**: `PipelineExceptionTranslatorPolicy` orders by `IsFallback` → `Order` → type name. Specific exceptions before catch-all. Overlap detection with diagnostics.
11. ~~**Permission/PermissionFor attributes**: Need removal for S365?~~ → **Resolved**: Already removed in prior session. Only Resource-based auth remains.
12. ~~**Custom S365 middleware blockers**: Any middleware Blueprint can't handle?~~ → **Resolved**: All 6 S365 middleware types have Blueprint equivalents (TenantModule, UoW, Outbox, RebusTransaction, ResourceExpansion, LinkGeneration). Health checks and CORS are standard ASP.NET Core, not Blueprint concerns.

---

## S365 Test Infrastructure Mapping

S365 testing uses `WebApiFixtureFactory<TStartup>` → `ApiFixture` with:

| S365 Pattern | Blueprint.Testing Equivalent | Notes |
|---|---|---|
| `WebApiFixtureFactory<TStartup>` | `BlueprintTestFixtureFactory<TApp,TExec>` | ✅ Abstract factory, `Create()` returns fixture |
| `apiFactory.Create()` | `factory.Create()` | ✅ Direct match |
| `apiFactory.Create(s => ...)` | `factory.Create(s => ...)` | ✅ Per-test DI overrides |
| `fixture.CommandAsync<T>(ICommand<T>)` | `fixture.CommandAsync<T>(ICommand<T>)` | ✅ Typed overload added |
| `fixture.QueryAsync<T>(IQuery<T>)` | `fixture.QueryAsync<T>(IQuery<T>)` | ✅ Typed overload added |
| `fixture.BackgroundTaskAsync(task)` | `fixture.BackgroundTaskAsync(task)` | ✅ Runs anonymous, IBackgroundTask overload |
| `fixture.MessageAsync(msg, retry)` | `fixture.MessageAsync(msg, retry)` | ✅ Retry support added |
| `fixture.Authentication.Set(identity)` | `fixture.Authentication.Set(identity)` | ✅ S365 API match |
| `fixture.Authentication.As(identity)` | `fixture.Authentication.As(identity)` | ✅ IDisposable scope |
| `fixture.Authentication.MakeAnonymous()` | `fixture.Authentication.MakeAnonymous()` | ✅ S365 API match |
| `fixture.Authentication.AsAnonymous()` | `fixture.Authentication.AsAnonymous()` | ✅ IDisposable scope |
| `fixture.GetOptions<T>()` | `fixture.GetOptions<T>()` | ✅ Added |
| `AuthenticateAs*()` extensions | Extension methods stay in S365 test helpers | S365-specific |
| `fixture.Bus` (FakeBus) | `(FakeBus)fixture.Services.GetRequiredService<IBus>()` | S365 extension method |
| `fixture.Tasks` | `fixture.Tasks` (BackgroundTaskCollector) | ✅ Direct match |
| `fixture.Messages` | `fixture.Messages` (TestMessageCollector) | Bus-agnostic capture |
| `ConfigureOnce()` | `ConfigureOnceAsync()` | ✅ Factory pattern |

**Key decisions to minimize S365 test churn:**
- `BlueprintTestFixture` has `ICommand<T>`/`IQuery<T>` typed overloads matching S365's `ApiFixture` exactly
- `BackgroundTaskAsync` runs anonymously (no auth) matching S365 behaviour
- `MessageAsync` supports `retryAttempts` parameter matching S365's Polly-based retry
- `TestAuthenticationManager` has `Set(ClaimsIdentity)`, `As(identity)`, `MakeAnonymous()` matching S365's `Authentication`
- `BlueprintTestFixtureFactory` mirrors `WebApiFixtureFactory` — xUnit `IClassFixture`, `Create()`, `ConfigureOnceAsync`
- S365 `AuthenticateAs*()` extension methods stay in S365 test helpers (they target `BlueprintTestFixture` instead of `ApiFixture`)
- FakeBus access: S365 adds an extension method `fixture.Bus` that resolves from DI
- `GetOptions<T>()` added to fixture directly

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
| 2025-07 | Current | ReadJsonBodyAsync static lambda, S365 test fixture compat shims (typed overloads, anonymous BackgroundTask/Message, auth API match, factory pattern, GetOptions), migration-findings updates |
| 2025-07 | Pipeline extraction | UoW/Outbox/BackgroundTask helper extraction, x-links/InvokeAsync/JsonConverter gap resolution, comprehensive S365 compatibility verification |
| 2025-07 | ResourceEventAudit | Extracted inline resource-event audit code to PipelineHelpers.ExecuteResourceEventAuditSinkAsync; self-link dedup test added; pipeline optimization analysis confirmed most code paths well-optimized |
