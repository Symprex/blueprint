# Signature 365 → Blueprint Source-Gen: Migration Findings

> Living reference for the Signature 365 migration.
> This version keeps the quick-reference tables, removes session history, and consolidates duplicate findings by domain.

## Status Summary

| Area | Status | Notes |
|------|--------|-------|
| Compile-time proof | ✅ Done | `.scratch/s365-proof/` compiles 11 services, ~72 ops (Analytics, EventStore, Templates, Generation, Outlook, VCard, ETA, Billing, Subscriptions, Microsoft365, Import) |
| Authorization model | ✅ Verified | 349 `[Resource]` usages and 158 `[FromResource]` usages map directly to source-gen Blueprint |
| Scope-pair provider | ✅ Done | `ScopePairClaimAuthorizationProvider` now matches S365 (`"::"`, CRUD hierarchy, ignored scopes, `allowWhenNoScopeClaims`) |
| Claims-based auth removal | ✅ Done | Legacy claims-only authorization layer was removed; resource-based auth is the only path |
| First-class tenancy | ✅ Done | `TenantModule` replaces runtime tenant population at compile time |
| TenantId OpenAPI exclusion | ✅ Done | `TenantIdConventionHelper` classifies auto-populated `TenantId` as `AuthContext` and removes it from params/body |
| Tenant spoofing prevention | ✅ Done | Query/body cannot override claim-derived `TenantId` unless route-owned or explicitly bound |
| Testing host/executor parity | ✅ Verified | `BlueprintTestHost.ExecuteAsync()` maps 1:1 to S365 `TestApiOperationExecutor.ExecuteAsync()` |
| Fixture/factory compatibility shims | ✅ Done | `BlueprintTestFixture`, factory, auth, bus, task, and Marten helpers cover S365-style test APIs |
| OpenAPI base shape | ✅ Verified | `operationId`, tags, `x-links`, enum metadata, request/response `$ref`, and error schemas match S365 expectations |
| TypeScript generation hard gate | ✅ Analysed | S365 uses custom Handlebars generators in admin/portal/partners; required compatibility flags are known |
| OpenAPI well-known endpoints | ✅ Done | `/.well-known/openapi` (RFC 9264) and `/.well-known/api-catalog` (RFC 9727) are built in |
| Serialization | ✅ Done | STJ pipeline, NodaTime support, ResourceEvent envelope fixes, and serializer-context wiring are complete |
| Marten integration | ✅ Done | `IUnitOfWork`, scoped tenant sessions, and outbox support are in place |
| Rebus integration | ✅ Done | Auto-detecting modules, transaction scope, subscriptions, and handler discovery are ready |
| Hangfire integration | ✅ Done | Schedule providers, recurring tasks, and queue selection are supported; S365 keeps custom partitioned storage/admin ops |
| HTTP client / gateway support | ✅ Done | `CallAsync` shims, `CallOptions`, JWT forwarding, `StatusCodeResult`, `PlainTextResult`, `ProxyResult`, and timeout mapping are supported |
| Handler discovery | ✅ Verified | Inline `Invoke`/`InvokeAsync`/`Handle`/`HandleAsync` plus external handler classes all work |
| ApiOperations base class | ✅ Verified | `Context`, `Identity`, `Logger`, `CancellationToken`, `HttpContext`, `HttpRequest`, and `HttpResponse` map directly |
| ResourceEvent contract | ✅ Verified | `ResourceEventResult<T>` with `[ResourceEventFamily]`/`[ResourceEventReason]` source-generated records; handlers return event records that auto-convert |
| Compile-time diagnostics | ✅ Done | Auth, property ownership, HTTP, link, validation, result, and serializer diagnostics cover the migration surface |
| ApiExceptionFactory resolution | ✅ Done | Field references (e.g. `CommonApiExceptions.NotFound`) resolved via semantic model; no BP010 warnings for S365 pattern |
| BP001 ApiResource partial | ✅ Done | Downgraded from Error to Warning; non-partial resources work with virtual `Object` fallback |
| Codemod run | ✅ Done | 10 BPMIG diagnostics found across 158 projects; code fixes exist for redundant setup and missing `partial` |
| Pipeline helper extraction | ✅ Done | JSON, auth, validation, UoW, outbox, HATEOAS, and audit steps are centralized in `PipelineHelpers` |
| Runtime hot-path optimization | ✅ Done | Binding helpers, async fast-paths, pre-sized collections, cached API catalog, and auth structs reduce overhead |
| Benchmarks and load tests | ✅ Done | 7 benchmark classes, 7 auth benchmark scenarios, and 9 NBomber scenarios documented |
| Profiling guide | ✅ Done | `docs/performance-profiling.md` is the source of truth for deeper profiling workflow |

## S365 Scale

| Metric | Count / Note |
|--------|--------------|
| Total projects | ~158 |
| Blueprint-consuming services | 16+ services; later full-service inventories counted 18 service hosts |
| SMTP subsystem | 59 projects; excluded from Blueprint migration scope |
| Operations | 632 repo-wide in early inventory; later HTTP-focused migration audits use ~540 |
| Commands | 342 repo-wide; later HTTP-focused audits use 294 |
| Queries | 290 repo-wide; later HTTP-focused audits use 246 |
| `[Resource(...)]` usages | 349 |
| `[FromResource(...)]` usages | 158 |
| Unique nouns in active use | 81; S365 also defines 96 noun constants |
| Operations using `ApiId` | 530 |
| `TenantId` properties | 281 auto-populated in migration-focused audits; 354 total `TenantId` occurrences across broader scans |
| Services using `SetTenantMiddleware` | 9/18 in the full service inventory |
| Services using Marten | 12/18 |
| Services using Rebus | 17/18 |
| Services using HATEOAS links | 5/18 |
| Gateway projects | 5 |
| `IHandleMessages<T>` handlers | 68 confirmed in the full service scan; earlier spot audits reported ~70–85+ |
| Recurring scheduler implementations | ~16 |
| Committed `openapi.json` files | 21 |
| S365 test projects | 42 total; many are SMTP or legacy-framework tests rather than migration-critical HTTP tests |
| Proof-project generated files | 183 (63 Blueprint + 120 STJ) |
| Estimated pipeline IL savings | ~25,000+ lines across the ~540 HTTP operation set |

> Count discrepancies in the original file came from different audit scopes. This rewrite keeps both numbers where they were materially different and explains the scope change.

## Authorization

### Resource attributes and compatibility
S365 uses resource-based authorization almost exclusively. The migration surface is stable: 349 operations use `[Resource(noun, verb, resourceKeyTemplate)]`, 158 use `[FromResource(typeof(X), template)]`, and the new Blueprint keeps those shapes unchanged apart from namespace updates. `[AllowAnonymous]` is already supported for 57+ operations, `[MustBeAuthenticated]` is unchanged, and there is no `Permission` / `PermissionFor` branch to preserve because source-gen Blueprint is intentionally resource-only.

| Authorization inventory | Count / note |
|---|---|
| `[Resource(...)]` usages | 349 |
| `[FromResource(...)]` usages | 158 |
| Unique nouns in active use | 81 |
| S365 noun constants defined | 96 |
| Most common template | `/Tenants/{TenantId}` (~115 usages) |
| Auth-context template | `/Tenants/{Auth:AccountId}` (~70 usages) |
| Multi-resource operations | ~20 OR-logic authorization cases |
| Anonymous HTTP operations | 57+ |

### Scope-pair provider and S365 defaults
`ScopePairClaimAuthorizationProvider` is the direct S365 replacement for `ScopeApiAuthoriser`. The critical compatibility details are now baked in: S365 uses `"::"` as the separator, `Manage` expands to `Create/Read/Update/Delete`, and the provider exposes `allowWhenNoScopeClaims: true` for S365’s “no scopes means full access” model. The implementation also preserves the ignored-scope behavior for `Signature365.ReadWrite.All`, `Signature365ReadWrite`, `Signature365Admin.ReadWrite.All`, `Signature365Admin.ReadWrite`, and `Signature365Partner.ReadWrite`.

```csharp
blueprint.AddResourceAuthorizationProvider(
    ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
        separator: "::",
        legacyScopeMapper: S365LegacyScopeMapper.Map,
        allowWhenNoScopeClaims: true,
        additionalIgnoredScopes:
        [
            "Signature365.ReadWrite.All",
            "Signature365ReadWrite",
            "Signature365Admin.ReadWrite.All",
            "Signature365Admin.ReadWrite",
            "Signature365Partner.ReadWrite",
        ]));
```

### Auth-path performance work
The auth hot path was de-duplicated and optimized for S365 scale. `ScopePair` and `ResolvedScopePair` are now `readonly record struct`s, single-scope operations use a zero-allocation `SingleItemList<T>` enumerator, `MatchesScopePair` uses span-based comparison, and cached `Task<bool>` results remove repeated `Task.FromResult` allocations. Separate auth benchmarks now cover exact match, hierarchy match, multi-scope scans, failure, and the S365-specific `allowWhenNoScopeClaims` fast path.

### Template expansion and compile-time validation
Blueprint’s `ResourceKeyTemplateParser` now covers the S365 token surface without reflection or regex. `{Auth:AccountId}`, `{Auth:Id}`, `{Auth:IdentityId}`, `{Auth:Email}`, and `{Auth:Name}` all resolve correctly, including S365’s `AccountId = TenantId ?? PartnerId ?? "Symprex"` rule. Property tokens such as `{Id}` are rendered as inline interpolation, value types use `.ToString()`, and invalid property references fail at compile time via `BPAU004` instead of surfacing as runtime auth bugs.

### Context loading and S365-owned auth concerns
Blueprint’s `IUserAuthorisationContext` is a 1:1 fit for S365’s context shape: `IsActive`, `IsAnonymous`, `Id`, `IdentityId`, `AccountId`, `Email`, `Name`, and `PopulateMetadata` all carry over directly. S365-specific JWT parsing stays consumer-owned: `Signature365UserAuthorisationContextFactory`, `IS365ResourceKeyExpander`, `IRoleToApiOperationMapper`, legacy scope remapping, role-group expansion, and wildcard resource-key behavior remain S365 concerns because they encode domain rules rather than Blueprint framework behavior.

### JWT claim inventory
The migration only needs the framework to pass through the claims S365 already uses. The important claim set is: `scope` (`Noun::Verb`), `sub`, `Sig365TenantId`, `Sig365PartnerId`, `Sig365IdentityId`, `Sig365RoleGroup`, `Sig365AssignedRole`, and `Sig365AssignedScopePair`. Blueprint now documents the recommended S365 registration pattern in `docs/authorization.md`, including the correct `"::"` separator and the full ignored-scope list.

| Claim / concept | S365 meaning | Blueprint seam |
|---|---|---|
| `scope` | Direct API authorization in `Noun::Verb` form | `ScopePairClaimAuthorizationProvider` |
| `Sig365TenantId` / `Sig365PartnerId` | Tenant/partner context and `AccountId` derivation | `IUserAuthorisationContext` + tenant resolver |
| `Sig365IdentityId` | Internal identity tracking | `IUserAuthorisationContext.IdentityId` |
| `Sig365RoleGroup` / `Sig365AssignedRole` | S365 role-group expansion | Consumer-owned auth-context factory / helpers |
| `Sig365AssignedScopePair` | Direct scope grants | Parsed into scope-pair authorization |
| AccountId fallback | `TenantId ?? PartnerId ?? "Symprex"` | `{Auth:AccountId}` template expansion |

### What stays S365-owned in authorization
Some auth pieces should remain explicitly outside Blueprint so the migration does not overreach. `Signature365UserAuthorisationContextFactory` keeps parsing S365-specific claims, `IS365ResourceKeyExpander` keeps handling hierarchical resource expansion, and `IRoleToApiOperationMapper` continues to power role-to-operation UI/reporting because it is a presentation concern that already reads `[Resource]` metadata directly.

| S365-owned auth concern | Why it stays outside Blueprint |
|---|---|
| `Signature365UserAuthorisationContextFactory` | Parses S365-specific JWT claims and account derivation rules |
| `IS365ResourceKeyExpander` | Encodes consumer-specific hierarchical resource expansion |
| Legacy scope mapper | Knows S365-specific scope renames and migration behavior |
| Role/group expansion helpers | Reflect S365 product-role semantics, not framework semantics |
| `IRoleToApiOperationMapper` | Drives UI/reporting over operation metadata rather than pipeline execution |

### Authorization audit notes worth preserving
The deeper audits all reached the same practical conclusion even when they used different wording: S365’s auth model is already a Blueprint-shaped model. There are no migration-blocking permission systems hiding behind the old runtime, the verb hierarchy matches exactly, wildcard expansion remains consumer-owned, and the outstanding work is registration plus context loading rather than attribute or handler rewrites.

## Tenancy

### `SetTenantMiddleware` → `TenantModule`
S365’s legacy `SetTenantMiddleware` did four things: read the tenant claim, set the ambient tenant accessor, enrich Serilog diagnostic context, and populate `operation.TenantId` when possible. The compile-time `TenantModule` now replaces the first, second, and fourth pieces without reflection. Serilog enrichment is intentionally left as ordinary ASP.NET Core middleware because it is logging infrastructure, not operation-planning logic.

### Tenant ownership rules and spoofing prevention
`TenantIdConventionHelper` is now shared between `TenantModule` and `PropertyOwnershipModule`, so auto-populated `TenantId` is treated as `AuthContext` consistently in both the pipeline and OpenAPI projection. When tenancy is enabled, properties named exactly `TenantId` are excluded from query/body ownership, kept out of OpenAPI inputs, and populated from the ambient tenant accessor unless the route owns the property or an explicit HTTP binding attribute says otherwise. That change also closed the spoofing hole where a query string could previously try to override the claim-derived tenant.

### Binding support for real S365 types
Tenant auto-population is no longer limited to `string`. The generated code now supports `string`, `IParsable<T>`, `TryParse`, and `[TypeConverter]` types, which matters because S365 commonly uses `ApiId` for `TenantId`. A minimal resolver is enough for the migration:

```csharp
public sealed class S365TenantResolver : ITenantResolver
{
    public string? ResolveTenantId(ClaimsPrincipal? principal)
        => principal?.FindFirst("Sig365TenantId")?.Value;
}
```

### What is and is not tenant-populated
The S365 property-population story is much narrower than the duplicated notes implied. There is no custom `IPropertyPopulator` layer to port: the meaningful custom behavior is only `TenantId`. `PartnerId` (~35–40 properties, depending on scope) and `UserId` (~8 properties) are route/body-bound rather than tenant-populated, so the regular property-ownership rules already cover them. This is why compile-time `TenantModule` + `PropertyOwnershipModule` replace the entire S365 populator story rather than merely part of it.

### Ambient tenancy and persistence
S365’s `ICurrentTenantAccessor` pattern maps directly to Blueprint `ITenantAccessor`, and the wider tenant model stays consumer-owned. Marten conjoined tenancy (`TenancyStyle.Conjoined`, `IHaveTenantId`, `store.IdentitySession(tenantId)`), S365’s tenant-specific auth-resource expansion, and message-body tenant propagation all remain application concerns layered on top of Blueprint’s ambient tenant seam.

| Tenancy fact | Consolidated finding |
|---|---|
| Services using `SetTenantMiddleware` | 9/18 in the full-service inventory; earlier narrower notes undercounted this |
| `TenantId` auto-population | 281 migration-critical operations; broader scans found 354 total `TenantId` properties |
| Non-tenant identity properties | `PartnerId` and `UserId` are ordinary route/body inputs, not tenant middleware responsibilities |
| OpenAPI effect | Auto-populated `TenantId` disappears from params/body unless explicitly bound or route-owned |
| Logging effect | Serilog `TenantId` enrichment still belongs in ASP.NET Core middleware |

### `TenantId` property frequency snapshot
The broad property audit is still useful because it shows why compile-time ownership matters. The most common operation properties were `TenantId`, `Id`, `PartnerId`, `Quantity`, `Name`, and `Country`, and only `TenantId` needed special ambient population logic. Everything else follows normal route, body, query, header, or cookie ownership.

### Practical migration note
The tenancy migration is therefore small but important: register an `ITenantResolver`, keep any tenant-aware logging middleware, let `TenantModule` set the accessor and operation property, and remove legacy `SetTenantMiddleware` registrations from services that previously added it.

## Testing

### Corrected primary test pattern
The most important testing correction is that S365’s real migration path is `TestApiOperationExecutor.ExecuteAsync()` → `BlueprintTestHost.ExecuteAsync()`. Earlier notes over-emphasized fixture/factory APIs; the later audit showed the dominant S365 pattern is executor-style operation execution, HTTP context creation, and optional per-call auth configuration. That is good news because the core execution shape maps 1:1.

### Fixture/factory compatibility surface
Blueprint.Testing still added a broad S365-friendly convenience surface because it reduces churn in mixed or future migrations. `BlueprintTestFixtureFactory<TApp, TExec>`, `BlueprintTestFixture<TApp, TExec>`, `QueryAsync`, `CommandAsync`, `BackgroundTaskAsync`, `MessageAsync(retryAttempts)`, `RunAllPublishedMessagesAsync`, and `RunAndConsumeAllPublishedMessagesAsync` all exist and intentionally mirror the older fixture-centric style.

| S365 pattern | Blueprint equivalent | Notes |
|---|---|---|
| `TestApiOperationExecutor.ExecuteAsync(op)` | `BlueprintTestHost.ExecuteAsync(op)` | Primary 1:1 mapping |
| `CreateStandalone(...)` | `BlueprintTestHostBuilder<TApp,TExec>.Create().Build()` | Standalone host |
| `CreateHttp(...)` | `BlueprintTestHostBuilder<TApp,TExec>.Create().BuildHttp()` | HTTP-enabled host |
| `fixture.QueryAsync<T>(query)` | `fixture.QueryAsync<T>(query)` | Convenience shim |
| `fixture.CommandAsync<T>(cmd)` | `fixture.CommandAsync<T>(cmd)` | Convenience shim |
| `fixture.BackgroundTaskAsync(task)` | `fixture.BackgroundTaskAsync(task)` | Clears auth, like S365 |
| `fixture.MessageAsync(msg, retryAttempts)` | `fixture.MessageAsync(msg, retryAttempts)` | Retry support preserved |
| `Authentication.Set / As / MakeAnonymous / AsAnonymous` | Same names on `TestAuthenticationManager` | API-compatible |
| `fixture.Bus` / `fixture.Tasks` | Same properties | `Bus` aliases `Messages` |
| `GetOptions<T>()` / `GetServiceAs<S,I>()` | Same helper shape | Safe test-double access |

### Auth, bus, and background-task helpers
The compatibility work intentionally kept the small ergonomic details that matter in large test suites. `BlueprintTestFixture.Bus` aliases `Messages`, `TestMessageCollector.Should()` returns `this` so FluentAssertions chains read like S365’s FakeBus helpers, `BackgroundTaskCollector.All` combines enqueued and scheduled work, and `WrapConfigureAnonymous` now uses a cached no-op delegate so anonymous task/message tests do not allocate a closure per execution.

### Exception and assertion behavior
`BlueprintTestHost.ExecuteAsync<T>` rethrows original exceptions using `ExceptionDispatchInfo`, so S365-style exception assertions still work without wrapper surprises. `BlueprintTestExecutionException` carries `ValidationErrors` and `IsValidationFailure`, the bus collector exposes `HavePublishedSingle<T>()`, `HavePublished<T>()`, `NotHavePublished<T>()`, `HaveSent<T>()`, and `HavePublishedAny<T>()`, and `BackgroundTaskAsync` / `MessageAsync` clear auth automatically to preserve S365 semantics.

### Marten testing helpers and S365-owned helpers
`Blueprint.Testing.Marten` now provides `WithDatabaseAsync` overloads, `LoadFromDatabaseAsync`, `AllAsync`, and `AllIncludingDeletedAsync`, which cover the common S365 per-tenant session patterns. The truly domain-specific helpers remain in S365: `AuthenticateAsTenantAdministrator`, partner/tenant user factories, signup/login flows, `ApiFixtureDbExtensions`, and any helpers that know S365 claim types, role groups, or tenancy key conventions.

### Testing scale and migration risk
The repo still reports 42 S365 test projects, but the migration-relevant HTTP surface is much smaller than that raw number suggests because many projects are SMTP-specific or tied to the legacy Blueprint framework. The corrected deep dive found only a few dozen files using the executor-style API directly, which means test infrastructure is not a blocker; the remaining work is mostly factory/host wiring rather than mass assertion rewrites.

| Earlier API-surface scan | Approximate count / note |
|---|---|
| `CommandAsync<T>` references | ~3,000 |
| `QueryAsync<T>` references | ~2,200 |
| `MessageAsync(...)` references | ~230 |
| Bus assertion chains | ~750 |
| `Authentication.As(...)` uses | ~1,500 |
| Background-task assertions | ~100 |

| Usage signal | Consolidated note |
|---|---|
| `ExecuteAsync(...)` | The corrected audit identified this as the primary S365 operation-test path |
| `CommandAsync` / `QueryAsync` | Added in Blueprint.Testing as convenience APIs even though the real migration mostly uses executor-style calls |
| Bus assertions | S365-style `HavePublished*` / `HaveSent*` patterns are fully covered |
| Background tasks | `BackgroundTaskCollector` matches S365’s observable task model, including `All` |
| Factory pattern | `BlueprintTestFixtureFactory` preserves the service-override / `IClassFixture` lifecycle pattern |

### What remains S365-owned in tests
This is the right place to be explicit about what not to port into Blueprint. Claim-building helpers such as `AuthenticateAsTenantAdministrator`, user-signup/login flows, tenant-aware Marten extensions, and any helpers that assume S365 role names or claim types stay in S365 test projects. Blueprint.Testing is deliberately assertion-library-agnostic and identity-model-agnostic.

### Migration cost summary for tests
Core test execution methods can stay conceptually unchanged, while the service bootstrapping objects change once per service. That is why the repeated “test churn” findings in the original file all reduced to the same message: the migration cost is in factory/host setup, not in thousands of assertion or method-signature edits.

## OpenAPI / TypeScript

### OpenAPI shape: what must match
The migration gate is not NSwag compatibility; it is raw OpenAPI JSON compatibility with S365’s custom Handlebars generators. The good news is that the critical shape already matches: `operationId` strips `Query`/`Command` suffixes just like S365, tags come from the last namespace segment, `x-links` uses `{ rel, operationId, method }`, `x-enumNames` is emitted, request and response bodies use `$ref` to component schemas, and nullable properties stay in OpenAPI 3.0-style `nullable: true` form.

| Aspect | Legacy S365 / NSwag | Blueprint source-gen | Migration note |
|--------|---------------------|----------------------|----------------|
| OpenAPI version | `3.0.0` | `3.0.4` by default | Set `OpenApiVersion = "3.0.0"` if tooling demands it |
| `operationId` | Class name without `Query`/`Command` | Same | Required by TS generator |
| Tags | Last namespace segment | Same | Verified against AdminPortal and Portal tag inventories |
| `x-links` | Present on schemas | Same structure | Required for HATEOAS client methods |
| Enum metadata | `x-enumNames` | `x-enumNames` + `x-enum-varnames` | Superset |
| Request body schema | Direct or `$ref` | `$ref` to components | Compatible |
| Response schema | Usually `200` | Semantic `2xx` + optional synthetic `200` | Requires flag for TS return types |
| Error content-type | `application/json` | `application/problem+json` by default | Use compat flag when needed |
| Generic schema naming | `TypeOfArg` pattern | Same after fixes | Critical for TS generation |
| `additionalProperties: false` | Present on object schemas | Present after fix | Needed for strict TS schemas |

### The required compatibility flags
S365’s TypeScript generator hardcodes `responses["200"]` when determining the success return type, so migrated services that return semantic `201` / `202` / `204` responses need the compatibility alias. For legacy error content-type compatibility, keep plain JSON errors enabled when the downstream frontend assumes them.

```csharp
app.MapBlueprintApi<App, AppExecutor>(
    configureOpenApi: o =>
    {
        o.OpenApiVersion = "3.0.0";                 // only if older tooling requires it
        o.UseStatus200ForSuccessResponses = true;   // hard gate for S365 TS generators
        o.UsePlainJsonForErrorResponses = true;     // when legacy clients expect application/json
    });
```

### S365’s actual TypeScript generation architecture
All three frontend apps (`admin`, `portal`, `partners`) use custom Handlebars generators, not NSwag. Each app has `api.handlebars`, `apiInner.handlebars`, `model.handlebars`, and `index.handlebars` under `frontend/apps/{app}/src/openapi/generator/`, driven by `index.mts`. The generator reads committed `Spec/openapi.json` files from disk, emits `api.ts` with model classes, AJV schemas, RTTI metadata, HATEOAS helpers, `setData()` constructors, and per-tag API classes, and is run through `npm run openapi`.

| Frontend app | Spec source | Generated output |
|---|---|---|
| admin | `src/Apps/AdminPortal/.../Spec/openapi.json` | `frontend/apps/admin/src/openapi/api.ts` |
| portal | `src/Apps/Portal/.../Spec/openapi.json` | `frontend/apps/portal/src/openapi/api.ts` |
| partners | `src/Apps/Partners/.../Spec/openapi.json` | `frontend/apps/partners/src/openapi/api.ts` |

| Template / helper | Purpose |
|---|---|
| `api.handlebars` | Top-level `api.ts` generation |
| `apiInner.handlebars` | Per-tag API classes |
| `model.handlebars` | Model/resource classes and enum metadata |
| `index.handlebars` | Entry-point generation |
| `pathGenerator` | URL/path construction |
| `ajvSchema` | AJV schema emission |
| `withOperation` | Links `operationId` back to operation metadata |
| `camelcase` / `requiresRtti` | Naming + RTTI generation helpers |

| Generator input | Why it matters |
|---|---|
| `operationId` | Becomes method names in generated API classes |
| `tags` | Groups operations into API classes |
| `responses["200"]` | Drives the success return-type inference |
| `x-links` | Generates typed HATEOAS helper methods and `can*` guards |
| `x-enumNames` | Preserves enum names for client-side model generation |
| Request-body schema | Distinguishes payload methods from parameter-only methods |
| Full component schemas | Feeds AJV validation and RTTI metadata |

### Tag and document inventory
The legacy and new tag algorithms are identical, which matters because tag changes rename generated API classes. Detailed audits found 25 unique tags in AdminPortal, 42 in Portal, and the same “last namespace segment” rule in both systems. The repo also has 21 committed `openapi.json` files, so spec diffing is a normal part of the S365 development workflow rather than a one-off migration exercise.

### Response-shape notes that are easy to lose
The TypeScript generator does not care about everything in the document equally. It ignores property-title trivia, tolerates empty-description omission, and does not require OpenAPI 3.1 features, but it does care about body-bearing `200` responses, `nullable: true`, named component schemas such as `ResourceEventOfFoo`, and stable schema-key shortening. Keeping those items explicit prevents re-introducing the same compatibility discussions later.

### Concrete schema and document fixes already applied
The long duplicate trail in the original document all collapsed to the same cluster of OpenAPI fixes. Generic component names now use the `TypeOfArg` pattern correctly, generic responses now point at the right `$ref`, `ResourceEventOf{T}` is emitted as a named component instead of being inlined incorrectly, enum-valued body and response properties render as `oneOf: [{ $ref: ... }]`, and `additionalProperties: false` is emitted for object schemas with declared properties. The renderer also handles `[JsonExtensionData]`, `Dictionary<string, string[]>`, RFC-compliant `ProblemDetails` `type`/`title`, the `Link` schema component, and request-body `$ref` emission.

| OpenAPI fix cluster | Why it mattered |
|---|---|
| Generic schema key normalization | Prevented malformed keys such as truncated generic names |
| Generic response `$ref` projection | Stopped body schemas disappearing from responses |
| `ResourceEventOf{T}` named components | Kept TS generators aligned with the real HTTP envelope |
| Enum component refs | Avoided inline enum duplication and preserved stable client codegen |
| `additionalProperties: false` | Preserved strict typed-object generation in frontend models |
| Request-body `$ref` rendering | Matched the way S365’s generator already resolves schema references |

### NodaTime, tags, and other shape-sensitive details
OpenAPI projection now recognizes eight NodaTime types without adding a NodaTime package dependency to the runtime library. `Instant`, `OffsetDateTime`, `ZonedDateTime`, and `LocalDateTime` map to `string/date-time`; `LocalDate` to `string/date`; `LocalTime` to `string/time`; and `Duration`, `Period`, and `Offset` to `string/duration`. Tag derivation remains identical to S365’s legacy code (`namespace.Split('.').Last()`), which produced 25 unique tags in AdminPortal and 42 in Portal during the detailed audits.

### `$links`, link metadata, and HATEOAS expectations
The TypeScript templates depend on both the `$links` payload property and the `x-links` OpenAPI extension. Blueprint now provides both in the same shape S365 expects: `$links` is serialized from `ApiResource.Links`, `x-links` lists non-self links as `{ rel, operationId, method }`, and the `Link` component schema exposes `href` plus nullable `type`. That is enough for the existing templates to generate `can{Operation}` guards and typed link-following methods without a frontend rewrite.

### Disk export and well-known endpoints
The new runtime serves the document at `/.well-known/openapi` and the API catalog at `/.well-known/api-catalog`, and it also supports the S365-friendly disk export workflow through `host.OutputOpenApi()`. That mirrors the committed-spec pattern already used in the S365 repo, where 21 `openapi.json` files are checked in under service `Spec/` folders and treated as generator inputs for the frontends.

### Known differences that are real but manageable
A few differences are real and should stay visible in the migration notes because they affect consumers. The TypeScript generator only reads `responses["200"]`, which is why the synthetic success alias matters. Query-string generation in the new client path uses camelCase property names instead of the PascalCase query parameters used by legacy Blueprint, so any frontend code that hand-builds URLs needs to be updated. Property-title emission and empty-description omission may produce diffs, but the templates do not depend on those fields.

| Legacy response-code shape | Confirmed distribution from older S365 output |
|---|---|
| Standard success document | `200, 500, 422` on ~140 operations |
| Not-found variant | `404, 200, 500, 422` on ~30 operations |
| Bad-request variant | `400, 200, 500, 422` on ~17 operations |
| Body-bearing created/update commands | Still represented as `200` in legacy docs |

### Representative tag inventories
The detailed tag inventories are worth preserving because tag changes rename generated API classes. AdminPortal’s 25-tag set included areas such as Accounts, Authorisation, BusQueues, Config, Import, Invoices, Microsoft365, PartnerUsers, Tenants, TenantUsers, and TransactionalEmail. Portal’s 42-tag set expanded that list with areas such as Analytics, Campaigns, Dashboard, ExchangeTransportAgent, Folders, Outlook, Roles, Rules, Search, Signatures, Surveys, Sync, Templates, Users, and VCard.

### Verification workflow per migrated service
The final migration gate remains a per-service spec and client diff. Generate the new OpenAPI document, compare it with the committed baseline, run the existing Handlebars generator unchanged, and inspect the resulting `api.ts` diff rather than assuming theoretical compatibility is enough.

```bash
curl http://localhost:5000/.well-known/openapi.json > openapi-new.json
diff src/Apps/Portal/.../Spec/openapi.json openapi-new.json
cd frontend/apps/portal
npm run openapi -- ../../../openapi-new.json
diff src/openapi/api.ts src/openapi/api.ts.bak
```

## Serialization

### S365 JSON baseline
S365 already uses `System.Text.Json` for HTTP payloads and keeps Newtonsoft.Json only for Marten persistence. The important baseline settings are camelCase naming, case-insensitive property lookup, `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, `JsonStringEnumConverter`, and NodaTime configuration through `ConfigureForNodaTime(DateTimeZoneProviders.Tzdb)`. S365 also carries a small set of application-owned converters such as `ApiId`, `FolderReference`, `Role`, `ObjectValue`, `BigInteger`, and `ProblemDetails`.

### Serializer-context wiring and the read-only options fix
Blueprint’s `ConfigureJson()` and serializer-context flow now matches what migrated services need. A key bug was fixed in `ApplySerializerOptions`: the runtime no longer freezes shared `JsonSerializerOptions` prematurely when building the context, and instead creates the context from a copy while composing resolvers safely. One operational gotcha remains worth keeping in this doc: do not build or consume a serializer context from app JSON options before `MapBlueprintApi`, because the options become read-only once the app pipeline is locked.

### Handler and ResourceEvent migration pattern

**This is the highest-churn migration step**, affecting ~257 ICommand operations and all handler files.

**Operations**: S365 declares `ICommand<ResourceEvent<T>>` (from `Blueprint.Http`). New Blueprint requires `ICommand<ResourceEventResult<T>>` (from `Blueprint.Runtime.Exec`). This is a mechanical find-and-replace across all command operations.

**Handlers**: S365 implements `IApiOperationHandler<TOperation, TResult>`. New Blueprint uses convention-based discovery — plain classes with `Handle`/`HandleAsync`/`Invoke`/`InvokeAsync` methods. The interface does not exist in new Blueprint; handlers just declare a method accepting the operation type and returning the result type.

**ResourceEventFamily pattern**: New Blueprint uses `[ResourceEventFamily]` + `[ResourceEventReason]` on partial record types. The source generator creates implicit conversion operators so handlers return event records that auto-convert to `ResourceEventResult<T>`:

```csharp
// Declare the family (one per resource type)
[ResourceEventFamily(typeof(TemplateApiResource), ResourceObject = "template")]
public static partial class TemplateResourceEvents
{
    [ResourceEventReason(ChangeType.Created, "created")]
    public sealed partial record Created(TemplateApiResource Resource);

    [ResourceEventReason(ChangeType.Updated, "updated")]
    public sealed partial record Updated(TemplateApiResource Resource);
}

// Handler returns the event record (auto-converts to ResourceEventResult<T>)
public ResourceEventResult<TemplateApiResource> Handle(CreateTemplateCommand cmd)
    => new TemplateResourceEvents.Created(new TemplateApiResource { Id = "..." });
```

**BPRE006 diagnostic**: Resource types used in resource event families must have a public `Id` property. S365 resources that use `TenantId` or other identifiers need a computed `public string Id => TenantId;` property added.

**Codemod coverage**: The `ResourceEvent<T>` → `ResourceEventResult<T>` and `IApiOperationHandler` → convention-based handler changes are mechanical and should be handled by a codemod pass.

### ResourceEvent wire-format compatibility
Several duplicated findings all boiled down to one serialization requirement: S365’s `ResourceEvent` wire format must stay stable. The new runtime now writes the envelope directly with `Utf8JsonWriter`, emits `$object = "event"`, serializes `ResourceEventChangeType` as a string enum instead of `0/1/2`, and includes the full envelope surface (`eventId`, `changeType`, `created`, `resourceObject`, `href`, `data`, `metadata`, `secureData`, `changedValues`, `correlationId`). That keeps both HTTP output and generated OpenAPI schemas aligned with S365’s existing clients.

| ResourceEvent envelope field | Why it stays visible in the migration doc |
|---|---|
| `$object` | S365 clients expect the discriminator value `"event"` |
| `eventId` | Preserved event identity |
| `changeType` | Must serialize as enum text, not integer values |
| `created` | Preserved audit timestamp |
| `resourceObject` / `href` | Needed by HATEOAS-aware clients |
| `data` | Typed payload for the actual resource |
| `metadata` | Consumer-owned event metadata bag |
| `secureData` | Part of S365’s established wire shape |
| `changedValues` | Preserves partial-change information |
| `correlationId` | Required for downstream traceability |

### Body-reading and primitive fast paths
JSON body parsing now avoids unnecessary fallback work. Reference-type body readers explicitly guard `JsonTokenType.Null`, nullable primitives use direct `Utf8JsonReader.GetXxx()` paths instead of `JsonSerializer.Deserialize<T?>()`, and `ReadJsonBodyAsync` already passes the operation object into the populate delegate so generated lambdas can be `static` and closure-free. Those improvements matter at S365 scale because POST/PUT operations are common and the old document repeatedly flagged body-reading overhead.

### Buffering and serializer requirements
`SerializeResourceEventEnvelope` now uses a `[ThreadStatic]` pooled `ArrayBufferWriter<byte>` to cut per-request allocation churn on event-heavy services. Separately, the serializer contract requirement is now explicit and enforced: every app must include `ProblemDetails` and `ValidationProblemDetails` in its `JsonSerializerContext`, and `BP006` fails the build when they are missing.

| Serialization gotcha | Consolidated guidance |
|---|---|
| NodaTime at runtime | Keep `ConfigureForNodaTime(DateTimeZoneProviders.Tzdb)` in service JSON configuration |
| `Duration` formatting | S365 uses the NodaTime string format (`"2:30:00"`), not ISO-8601 duration literals |
| Custom converters | Keep S365-owned converters registered through the app serializer context / STJ options |
| Body-null handling | Reference-type bodies need explicit null-token handling; this is already built into generated readers |
| Problem details | Always include `ProblemDetails` and `ValidationProblemDetails` in the serializer context |

### Serialization summary
The combined effect of these changes is that migration risk is mostly configuration risk, not runtime-framework risk. S365 still owns some converters and Marten serialization choices, but the Blueprint runtime now preserves the wire formats and envelope shapes that the repo’s HTTP clients, OpenAPI generators, and persistence layers actually depend on.

| Converter / serializer concern | Ownership |
|---|---|
| `ApiIdJsonConverter` | S365-owned converter; works unchanged in STJ serializer context |
| `FolderReference` / `Role` converters | S365-owned |
| `ObjectValue` / dictionary-object converters | S365-owned |
| NodaTime serialization | Service configuration concern, not a Blueprint built-in package dependency |
| Marten `JsonNetSerializer` | S365-owned persistence choice |
| Problem-details JSON shape | Framework concern; now enforced and stable |

## Persistence (Marten / Rebus / Hangfire)

### Marten integration and what stays S365-owned
Blueprint now covers the framework-side Marten story through `IUnitOfWork` and `MartenUnitOfWork`, but it does not absorb application-specific persistence policy. S365 still owns conjoined tenancy (`TenancyStyle.Conjoined`, `IHaveTenantId`), the choice to use `JsonNetSerializer` for Marten JSONB storage, custom Npgsql handlers like `ApiId`, and any per-service registries or listeners such as `TimestampTrackedEntityListener`. The migration only needs Blueprint to cooperate with those choices, not replace them.

### Outbox and ordering
S365’s custom outbox remains application-owned, but the framework seams now line up cleanly. `MartenOutboxStorage`, `OutboxClientTransportDecorator`, and `OutboxForwarder` stay in S365, while Blueprint exposes `IMessageOutbox` and helper stages that preserve the right ordering. The final order is deliberately stronger than the legacy pipeline notes implied: save unit of work, commit message outbox, then complete the outer messaging scope, which is safer than committing messaging before persistence.

### Rebus handler and subscription compatibility
There is no handler rewrite to do for Rebus. S365’s `IHandleMessages<T>` classes stay unchanged, subscriptions are still declared with `Subscribe<T>()`, one-way clients and `IMessageContext` continue to work, and the source-generated path auto-detects the runtime types it needs. The startup transformation is the only meaningful change: replace legacy `apiBuilder.RebusConsumer(...)` patterns with `services.AddBlueprintRebusConsumer(...)`, and let the generated `RebusTransactionModule` and `MartenScopeModule` take over the transaction plumbing.

### Rebus routing and opt-outs
Blueprint.Rebus already carries the seams S365 needs for co-located routing configuration. `[NoRebusTransaction]` remains the compile-time opt-out, `BusMessageAttributeRouter` supports `[BusMessage("queue", topicName: "topic")]`, and S365 can keep its custom simplified topic-name convention without asking Blueprint to understand S365 namespaces. This is another place where the original file repeated the same conclusion in several sections: handler code is stable, startup wiring changes.

### Hangfire scheduling, queues, and recurring work
`Blueprint.Tasks.Hangfire` now covers the shared scheduling surface. `[Queue("portal")]` is honored, `BackgroundTaskEnvelope.GetQueueName()` reads queue attributes, `HangfireBackgroundTaskScheduleProvider` and `HangfireRecurringTaskProvider` map cleanly onto the S365 abstraction, and scheduler implementations keep the same `IRecurringTaskScheduler` interface. The notable migration wrinkle is registration style: source-gen Blueprint uses explicit `AddRecurringTaskScheduler<T>()`, so S365 needs one DI registration per scheduler implementation rather than runtime auto-discovery.

### What stays with S365 in Hangfire
The platform-specific Hangfire pieces do not belong in Blueprint and do not need to. S365 keeps `RecurringJobPartitionedSqlServerStorage`, its three admin operations (`TriggerScheduledMessageCommand`, `RequeueMessageCommand`, `DeleteMessageCommand`), its service-specific queue naming conventions, and any decision not to expose the Hangfire dashboard publicly. Blueprint only needs to schedule and execute through those seams, which it now does.

### Concrete recurring job inventory
The important recurring-job examples are still worth keeping in one place because they are representative migration tests: tenant hard delete, user-group hard delete, Graph webhook renewal, Entra ID import, domain-info refresh, and billing cleanup. Those all map cleanly onto the new scheduling abstractions and prove that S365’s recurring-workload shape is ordinary rather than exotic.

| Recurring/background workload | Typical S365 example |
|---|---|
| Tenant cleanup | `TenantHardDeleteRecurringTaskScheduler` |
| User-group cleanup | `UserGroupHardDeleteBackgroundTaskScheduler` |
| Graph subscription renewal | `GraphWebhookRecurringTaskScheduler` |
| Entra ID import | `Microsoft365RecurringTaskScheduler` |
| Domain refresh | `DomainInfoRefreshTask` |
| Billing cleanup | `BillingSoftDeleteScheduler` |

| Representative service inventory | Notes |
|---|---|
| Portal | Marten + Rebus + tenant middleware + HATEOAS + resource expansion |
| Billing | Marten + Rebus + resource events; no tenant middleware |
| Analytics | Rebus-heavy read-oriented service; no Marten |
| Partners | Marten + Rebus + tenant middleware + HATEOAS |
| Outlook | Marten + Rebus + tenant middleware |
| VCard | Marten + Rebus + tenant middleware + HATEOAS |
| Templates | Minimal host; no Marten, no HATEOAS |
| Generation | Marten + Rebus; no tenant middleware |
| IdentityServer | Rebus + auth-heavy startup; no Marten |
| EventStore | Rebus + tenant middleware; event-oriented host |
| Import | Marten + Rebus + tenant middleware |
| Microsoft365 | Marten + Rebus; background-task heavy |

| Service capability snapshot | Consolidated count |
|---|---|
| Services using Marten | 12/18 |
| Services using Rebus | 17/18 |
| Services using `SetTenantMiddleware` | 9/18 |
| Services using HATEOAS links | 5/18 |
| Gateway-style hosts | 5 |
| Scheduler implementations | ~16 |

### S365-owned infrastructure that should remain consumer-owned
Several persistence and messaging pieces are intentionally not Blueprint features: `RecurringJobPartitionedSqlServerStorage`, S365-specific outbox tables and forwarders, service-specific topic conventions, and admin operations over Hangfire state. The framework now provides the execution seams those pieces need, which is enough for migration without turning Blueprint into a copy of S365 infrastructure.

### Registration delta summary
The main migration delta is registration style, not behavioral support. Marten moves to explicit `AddUnitOfWork<MartenUnitOfWork>()`, Rebus subscriptions move to `AddBlueprintRebusConsumer(...)`, recurring schedulers become explicit `AddRecurringTaskScheduler<T>()` registrations, and the rest of the infrastructure stays consumer-owned behind those hooks.

| Legacy startup pattern | Source-gen equivalent |
|---|---|
| `.ConfigureCommon(AppDefinition.Instance)` | Keep `AppDefinition`, but register explicit Blueprint services instead of runtime conventions |
| `.Http(h => h.Json(...))` | `ConfigureJson(...)` + app serializer context |
| `.Operations(o => o.Scan(...))` | `[BlueprintAppScan]` on the app model |
| `.AddOpenApi(...)` | `MapBlueprintApi(configureOpenApi: ...)` |
| `.AddAuthentication(a => a.UseContextLoader<T>())` | Register auth context / auth provider through DI |
| `.AddAuthorisation()` | Implicit when auth-bearing operations exist; configure provider explicitly |
| `.Pipeline(p => p.AddMiddleware<SetTenantMiddleware>(...))` | `TenantModule` + `ITenantResolver` |
| `.UseRebusAndMarten()` | `AddUnitOfWork<MartenUnitOfWork>()` + Blueprint.Rebus + outbox seams |
| `.AddResourceEvents<T>()` | `AddResourceEventAuditSink<T>()` |
| `.AddHateoasLinks()` / `.UseResourceExpansion()` | Built-in link/expansion support plus app-owned expanders |

### Startup-audit conclusion
The service audit consistently found a uniform startup style across the S365 repo. That is why the migration guidance can stay concise: most services differ in which infrastructure seams they register, not in the structure of the migration itself.

## Pipeline Optimizations

### Shared helper extraction
Most of the old file’s pipeline findings collapsed into a simple summary: the source generator now emits compact orchestration and pushes the reusable heavy lifting into `PipelineHelpers`. That includes JSON body reading, authentication and resource authorization, validation, audit, tenant resolution, route/query/header/cookie binding, unit-of-work save, message-outbox commit, background-task flush, HATEOAS population, and ResourceEvent self-query resolution.

| Pattern | Extraction |
|---------|------------|
| JSON body reading | `ReadJsonBodyAsync` |
| Authentication / authorization | `CheckAuthenticationAsync`, `RunAuthorisersAsync`, `CheckResourceAuthorizationAsync` |
| Validation | Fast-path validators + `ValidateIValidatableObject<T>` |
| Audit | `ExecuteAuditSinkAsync`, `ExecuteResourceEventAuditSinkAsync` |
| Tenant resolution | `ResolveTenant` |
| Binding | `BindRouteValue<T>`, `BindQueryValue<T>`, `BindQueryArray<T>`, `BindHeaderValue<T>`, `BindCookieValue<T>` |
| Persistence | `SaveUnitOfWorkAsync`, `CommitMessageOutboxAsync` |
| Background tasks | `FlushBackgroundTasksAsync` |
| Links | `PopulateHateoasLinksAsync` |
| ResourceEvent post-processing | `ResolveResourceEventSelfQueryAsync`, consolidated post-success helpers |

### Estimated code-size impact
At S365 scale the helper extraction is not cosmetic. The migration-focused audits estimate roughly 25,000 saved lines of generated pipeline IL/text across the ~540 HTTP-operation set, including large wins from body reading, HATEOAS population, audit extraction, and shared post-success orchestration. The original file had many separate “optimization” entries, but they all pointed to the same conclusion: there is no remaining high-value extraction opportunity of the same size.

### Base-class field hoisting and executor shell work
Cross-cutting service lookups were hoisted out of individual generated pipelines and into the executor shell. `PipelineServices` and `OperationExecutorShell` now cache shared dependencies such as `ITenantAccessor`, `ITenantResolver`, `IPropertyPopulator[]`, audit sinks, authorisers, and resource-authorization providers, removing thousands of duplicate field declarations and repeated constructor resolution lines. The important invariant is unchanged: only storage and resolution moved, not pipeline-stage ordering.

### Generator-time performance improvements
Incremental generator work was trimmed across the board. `ToDisplayString` namespace churn was replaced with structural namespace checks, `CompilationOperationLookup` caches repeated `FindTypeByMetadataName` calls, validation caches `GetAttributes()` results, `DescriptorPlan.Equals` short-circuits on counts before `SequenceEqual`, `ExternalHandlerIndex` is dictionary-backed instead of linear, property-ownership scans were flattened into a single pass, and small list/collection helpers were rewritten to avoid conversion churn. These changes matter because S365 is large enough for incremental-generator costs to be visible.

| Generator/runtime optimization | Consolidated effect |
|---|---|
| `CompilationOperationLookup` cache | Eliminates repeated metadata-name lookups per compilation |
| `ExternalHandlerIndex` dictionary | O(1) handler lookups instead of linear scans |
| `DescriptorPlan.Equals` count check | Avoids unnecessary sequence comparisons |
| Attribute caching in validation | Reduces repeated `GetAttributes()` work |
| Single-pass property ownership | Avoids repeated scans through the same symbols |
| Pre-sized endpoint collections | Reduces startup reallocations for large apps |
| Cached API catalog bytes | Avoids re-rendering static discovery documents |
| Shared auth structs/lists | Reduces allocations on every authorized request |

### Runtime hot-path work
The runtime side also shed unnecessary work. Binding helpers use static parse delegates, several helper methods gained non-async fast paths to skip state-machine allocation on the common path, `LinkGeneratorHandler` avoids async machinery when there is no content to decorate, `CreateNested()` now preserves the parent data dictionary correctly, endpoint-registration collections are pre-sized from `BlueprintApp.OperationCount`, route collisions emit structured warnings, and the API catalog response is cached as pre-rendered bytes until the public host changes.

### Validation fast paths
The migration notes repeatedly revisited validation, but the consolidated result is straightforward: compile-time validation now covers the full S365 DataAnnotations surface with 43 fast-path strategies, zero reflection on success, lazy `List<ValidationResult>` creation, cached attribute access, cached regexes for `[RegularExpression]`, and a helper for lazy error accumulation. S365’s validation story remains DataAnnotations-based; there is no FluentValidation migration branch hiding elsewhere.

### Micro-optimizations called out explicitly
A few small optimizations were notable enough to keep even after consolidation. `ReadJsonBodyAsync` now supports static delegates; `FlushBackgroundTasksAsync` uses `ValueTask.IsCompletedSuccessfully` before calling `.AsTask()`; auth uses `SingleItemList<T>` for the common one-scope case; API catalog output is pre-rendered; and route+operation endpoint deduplication uses `HashSet<(Route, OperationType)>` with cross-operation collision logging.

### Benchmarks, load tests, and performance references
The performance evidence is broader than a single microbenchmark. The repo now includes seven benchmark classes, a dedicated `AuthorizationBenchmarks` suite with seven S365-realistic scenarios, and nine NBomber scenarios covering GET/POST/PUT/DELETE, validation, auth, OpenAPI, and health endpoints. The headline runtime numbers are still useful: minimal query ~1.9 µs / 2.51 KB, validated query ~13 µs / 5.12 KB, full-stack command ~14.3 µs / 5.73 KB, and hot-path link generation ~71 ns. For deeper investigation, `docs/performance-profiling.md` is the maintained reference.

| Benchmark snapshot | Mean | Allocated |
|---|---:|---:|
| Minimal query | ~1.9 µs | 2.51 KB |
| Validated query (valid) | ~13.0 µs | 5.12 KB |
| Full-stack command (valid) | ~14.3 µs | 5.73 KB |
| Link generation (typed resource) | ~71 ns | 208 B |

| Authorization benchmark scenarios | Purpose |
|---|---|
| Exact match | Single required scope |
| Hierarchy match | `Manage` satisfying CRUD |
| No scopes / full access | S365 `allowWhenNoScopeClaims` fast path |
| Many scopes / exact match | Realistic multi-scope principal |
| Many scopes / many required | Worst-case positive scan |
| Failure path | Worst-case negative scan |
| Unresolved scope pair | Legacy comparison path |

### Performance conclusion
The consolidated performance story is simple: the generated pipelines are already well-optimized, the remaining micro-optimizations are either startup-only or error-path-only, and there is no evidence that pipeline overhead is a migration blocker relative to normal network, host, or database latency.

## HTTP Client / Gateway

### `CallAsync`, `SendAsync`, and minimal migration churn
S365 gateways use `BlueprintHttpClient.CallAsync<TResponse>(IReturn<TResponse>, CallOptions?, CancellationToken)`, so the new client keeps `CallAsync` shims that delegate directly to `SendAsync` rather than forcing a mass rename. `CallOptions` is intentionally the same simple shape—`Action<HttpRequestMessage>? ModifyRequest`—and generated typed client extensions also carry `CallOptions` overloads so existing gateway call sites can stay almost unchanged.

### Token forwarding and registration pattern
The gateway auth story is also direct. `HttpClientTokenForwarding.ForwardAuthorizationHeader(sp, client)` now replaces S365’s older manual helper and forwards the inbound `Authorization` (and related request metadata) into downstream service calls. The new registration pattern is therefore concise and explicit:

```csharp
services.AddBlueprintHttpClient<BackendApp>((sp, client) =>
{
    client.BaseAddress = new Uri("https://backend/api/");
    HttpClientTokenForwarding.ForwardAuthorizationHeader(sp, client);
});
```

### Response handling and resilience
BlueprintHttpClient now covers the S365 result surface that was duplicated across several findings. It recognizes `StatusCodeResult` subclasses by matching their static `Instance` singleton, handles `PlainTextResult` for non-JSON payloads such as `text/vcard`, maps `TaskCanceledException` to `RequestTimeout`, maps `HttpRequestException` to `InternalServerError`, and uses cached property metadata plus efficient query-string building to avoid per-request reflection churn.

### ResourceEvent HTTP-client fix
A real compatibility gap did exist for typed `ResourceEvent<T>` HTTP responses. Because the base `ResourceEvent.Data` property is `object?` and the typed `new TResource? Data` property is ignored during default STJ deserialization, the client could surface `JsonElement` instead of `TResource`. The fix is explicit envelope parsing in `SendResourceEventEnvelopeAsync<TResource>()`, which reconstructs a properly typed `ResourceEvent<TResource>` including `$object`, event metadata, and the typed `data` payload.

### Gateway patterns proven in S365
The gateway architecture is not exotic and no longer needs to be treated as a risk area. The five confirmed gateway projects cover the main patterns the framework must support: plain forwarding, multi-service aggregation, fallback/error handling, file-upload forwarding, and response-header shaping such as cache control. `ProxyResult` is now a built-in `HttpResult` subtype for downstream pass-through responses, so gateway code does not need a private compatibility wrapper.

| Gateway | Pattern |
|---------|---------|
| Import.Gateway | File upload forwarding |
| ExchangeTransportAgent.Gateway | Multi-service aggregation |
| Partners.Gateway | Service forwarding |
| VCard.Gateway | Fallback/error-handling gateway |
| Microsoft365.Gateway | Service forwarding |

### Typed client generation and coexistence
S365’s custom `ServiceApiSourceGenerator` is orthogonal to Blueprint source generation, not a blocker. It generated typed interfaces over `BlueprintHttpClient.CallAsync`, while new Blueprint can generate typed extension methods over `BlueprintHttpClient<TApp>`. Both patterns can coexist during migration, and the framework now routes typed `ResourceEvent<T>` returns through the manual envelope parser so those generated client layers do not lose fidelity.

| HTTP-client fact | Consolidated finding |
|---|---|
| `CallAsync` usage | The corrected audit found 11 production usages, all in generated or typed-client HTTP paths rather than test infrastructure |
| `CallOptions` | Same `ModifyRequest` shape as S365 |
| Query-string casing | New generated clients use camelCase query keys; legacy Blueprint URLs were PascalCase |
| `StatusCodeResult` handling | Reflection-based `Instance` matching keeps singleton result semantics |
| `PlainTextResult` handling | Needed for S365 text/vcard and similar non-JSON responses |
| `ProxyResult` | Built-in `HttpResult` for downstream response pass-through |

### Gateway migration summary
The repeated gateway findings all resolve to the same practical rule: keep forwarding/aggregation logic in S365, keep typed client generators if they still provide value, and let Blueprint.HttpClient own transport mechanics such as URL generation, token forwarding, error mapping, and `ResourceEvent` envelope parsing.

| Gateway behavior | Supported path |
|---|---|
| Plain forwarding | `BlueprintHttpClient<TApp>` request generation + response handling |
| Multi-service aggregation | Consumer-owned handler logic composing multiple downstream calls |
| Fallback on downstream failure | Consumer-owned `try/catch` around client calls |
| Downstream pass-through response | `ProxyResult` |
| JWT forwarding | `HttpClientTokenForwarding.ForwardAuthorizationHeader(...)` |
| Typed client wrappers | Keep S365 generator or move gradually to Blueprint-generated extensions |

### Hosting details that stay outside Blueprint
Health checks and CORS are still ordinary ASP.NET Core concerns rather than Blueprint-specific migration items. S365 can keep `services.AddHealthChecks()` and `MapSig365HealthChecks()` alongside `MapBlueprintApi()`, preserve the existing `/health`, `/health/readiness`, and `/health/liveness` layout, and continue to use any separate `HEALTHCHECK_PORT` deployment pattern without framework interference.

## Operation Patterns

### Handler discovery: inline and external patterns both work
S365 uses two HTTP handler shapes in meaningful numbers: inline operation methods (`Invoke`, `InvokeAsync`, `Handle`, `HandleAsync`) and external `*Handler : ApiOperations` classes whose first method parameter is the operation type. Blueprint’s handler discovery now supports both without an `IApiOperationHandler<T>` interface, and the later audits confirmed that this covers the real S365 mix rather than just the sample app.

| Handler pattern | Consolidated count / note |
|---|---|
| Inline operation handlers | ~247 in the deeper Portal audit |
| External `*Handler : ApiOperations` classes | ~81 in the deeper Portal audit |
| Rebus `IHandleMessages<T>` handlers | 68 confirmed in the full service scan |
| Supported method names | `Invoke`, `InvokeAsync`, `Handle`, `HandleAsync` |
| Handler constructor DI | Rich constructor injection is supported; multiple infra services are fine |

### `ApiOperations` base class parity
`ApiOperations` is a direct migration seam rather than a rewrite point. Blueprint injects `Context`, `Identity`, `Logger`, `CancellationToken`, `HttpContext`, `HttpRequest`, and `HttpResponse` in the same shape S365 expects, and it also auto-detects those property injections across the type hierarchy. That means handler logic that depends on the base class can usually move with only namespace updates.

| Injected member | Migration note |
|---|---|
| `Context` | Same `ApiOperationContext` concept and usage |
| `Identity` | Same `IUserAuthorisationContext` seam |
| `Logger` | Same handler-level logger pattern |
| `CancellationToken` | Same pipeline-injected cancellation source |
| `HttpContext` | Computed from `Context`, no separate migration seam |
| `HttpRequest` / `HttpResponse` | Same convenience surface as legacy `ApiOperations` |
| `ClaimsPrincipal` | Available through handler/property injection when needed |

### Return-type compatibility
The return-type audit is stronger than the original document’s scattered notes suggested. `ResourceEvent<T>`, `ResourceCreated<T>`, `ResourceUpdated<T>`, `ResourceDeleted<T>`, `StatusCodeResult.Accepted/Created/NoContent/OK`, `PlainTextResult`, `HttpResult`, plain `ApiResource` responses, `ListApiResource<T>`, `PagedApiResource<T>`, and S365-owned list subclasses all work. S365 does not use `Results<T1,T2>` unions in practice, and `IReturn<T>` is largely a client-side concern rather than the dominant operation interface shape.

| Return-type inventory | Count / note |
|---|---|
| `ResourceEvent<T>` commands | 254 in the deeper command audit; the dominant command return type |
| `StatusCodeResult.Accepted` | 19 |
| `StatusCodeResult.NoContent` | 2 |
| `StatusCodeResult.Created` | 1 |
| `StatusCodeResult.OK` | 1 |
| Direct API-resource query returns | 170+ |
| `ListApiResource<T>` queries | 40+ |
| `PagedApiResource<T>` queries | 17 |
| `PlainTextResult` queries | 9 |

### ResourceEvent conveniences and audit seams
The legacy ResourceEvent API surface survives in the new model. Convenience factories (`ResourceCreated<T>`, `ResourceUpdated<T>`, `ResourceDeleted<T>`), self-query constructors, `ResourceEventDefinition.For(new Query())` inference from `IQuery<T>`, and `IResourceEventAuditRepository`-style sinks are all available. The runtime now recognizes `Blueprint.Http.ResourceEvent<T>` as a valid declared result contract, uses the full envelope schema in OpenAPI, and passes the full `OperationResult` into `IResourceEventAuditSink` so S365 can keep publishing audit events through its existing bus-aware repository logic.

### Control attributes and compile-time behavior
The control-attribute story is complete and does not need to be rediscovered service-by-service. `[DoNotSaveChanges]` removes the UoW step and is guarded by `BPP001`, `[NoRebusTransaction]` skips the Rebus transaction wrapper, `[DoNotAudit]` strips audit work at compile time, `[Sensitive]` emits ordered sensitive-field metadata, `[AlwaysExpand]` forces resource expansion, `[AllowAnonymous]` short-circuits auth checks, and `[UnexposedOperation]` keeps internal operations out of the HTTP host and OpenAPI document.

| Attribute | Consolidated behavior |
|-----------|------------------------|
| `[DoNotSaveChanges]` | Compile-time UoW skip; guarded by `BPP001` |
| `[NoRebusTransaction]` | Compile-time Rebus transaction opt-out |
| `[DoNotAudit]` | Compile-time audit-step removal |
| `[Sensitive]` | Ordered sensitive-field metadata emitted at compile time |
| `[AlwaysExpand]` | Compile-time unconditional expansion |
| `[AllowAnonymous]` | Compile-time auth short-circuit |
| `[UnexposedOperation]` | Hidden from HTTP exposure and OpenAPI |

### Link generation, expansion, and link checkers
S365’s higher-level HATEOAS patterns all map cleanly now. `[Link]` and `[SelfLink]` are supported, `StateBasedLinkChecker<TOperation, TResource>` has the same API S365 expects, resource expansion works through `IResourceExpander<T>`, and self-link duplication in generated pipelines was removed so multiple operations that declare the same self route do not emit duplicate `AddLink("self")` calls. The shared host-exposure helper also centralized route extraction across link and HTTP metadata flows.

### Nested execution and executor flow
Nested execution is not a missing feature. Blueprint already supports `ApiOperationContext.CreateNested(...)`, `ExecuteAsync(...)`, and `ExecuteWithNewScopeAsync(...)`, which covers S365’s internal operation chaining in places like task dispatch and post-create resource fetches. The `CreateNested()` data-dictionary bug called out in the original file was fixed so nested contexts now share the parent data bag correctly.

### ApiId, binding, and compile-time ownership
`ApiId` deserves one clear paragraph rather than several scattered notes. Blueprint route/query binding already supports `[TypeConverter]` types through `TypeDescriptor.GetConverter(...)`, JSON body binding works through the S365-owned `JsonConverter<ApiId>`, and tenant auto-population can use the same type path. The only remaining recommendation is performance-related: add `TryParse(string?, out ApiId)` or `IParsable<ApiId>` to unlock the allocation-free fast path and eliminate the informational `BPPO011` diagnostic.

```csharp
public static bool TryParse(string? value, out ApiId result)
{
    if (TryHydrate(value, out var apiId))
    {
        result = apiId;
        return true;
    }

    result = default;
    return false;
}
```

### Patterns that are explicitly not blockers
Two often-repeated “gaps” are now explicitly closed out. First, S365 does not rely on custom message-population sources or property-populator interfaces beyond `TenantId`, so there is no runtime populator subsystem to recreate. Second, `IFormFile` / multipart handling is not a migration blocker because the S365 operations audit found no relevant non-SMTP usage that would force a first-class framework change.

### Binding and operation-shape details worth preserving
A few smaller compatibility fixes are easy to lose if the document is too aggressively compressed. `[TypeConverter]` route/query binding works today, but `ApiId` would be faster with `TryParse`; `[FromAuthContext(ClaimType = ...)]` exists for non-tenant auth-populated properties; `QueryOrderDefinition` now accepts the nullable-bool constructor form S365 uses; and handler DI parameter injection works for rich constructors, including S365 handlers with multiple infrastructure dependencies.

### S365-owned generators and adjacent tooling
S365’s `DtoSourceGenerator`, `MetricsSourceGenerator`, and legacy `ServiceApiSourceGenerator` are orthogonal to Blueprint source generation rather than blocked by it. Likewise, the old `Blueprint.Compiler` runtime Roslyn compiler is simply superseded by source generation; it is historical context, not a migration feature gap.

| Resource / helper type | Migration stance |
|---|---|
| `ListApiResource<T>` | Built into Blueprint |
| `PagedApiResource<T>` | Built into Blueprint |
| `ContinuationPagedApiResource<T>` | Built into Blueprint |
| `TenantListApiResource<T>` | S365-owned thin wrapper over shared collection types |
| `PartnerListApiResource<T>` | S365-owned thin wrapper over shared collection types |
| `ResourceEvent<ListApiResource<T>>`-style nesting | Supported; no longer considered a gap |

| Adjacent generator / tool | Migration stance |
|---|---|
| `DtoSourceGenerator` | S365-specific; no Blueprint overlap |
| `MetricsSourceGenerator` | S365-specific; no Blueprint overlap |
| `ServiceApiSourceGenerator` | Optional coexistence path until generated HTTP-client extensions fully replace it |
| `Blueprint.Compiler` | Historical runtime compiler; superseded by source generation |

### Resource and collection model notes
The collection and resource types that show up repeatedly in S365 are already present where they need to be. `ListApiResource<T>`, `PagedApiResource<T>`, and `ContinuationPagedApiResource<T>` are part of Blueprint; tenant- and partner-specific list wrappers stay in S365 when they encode S365 semantics rather than generic transport concerns. This is also why `ResourceEvent<ListApiResource<T>>` and similar nested shapes are no longer treated as gaps.

## Compile-Time Diagnostics

### Authorization and resource-template diagnostics
The migration now fails early when auth metadata is wrong. `BPAU004` reports nonexistent template-property references, `BPAU008` warns when resource attributes are placed on non-HTTP operations where they have no effect, and `BPAU003` covers special-permission validation such as anonymous/authenticated markers. This replaces a large class of runtime surprises that the legacy stack could only detect indirectly.

### Property-ownership diagnostics
The property-ownership diagnostics are important because they describe most migration mistakes precisely. `BPPO006` catches mixed ownership such as `[FromAuthContext]` plus `[FromBody]`, `BPPO009` flags claim-based auth mapping on non-string properties, `BPPO010` warns when auth and route ownership overlap, `BPPO011` points out TypeConverter-only binding paths that would be faster with `TryParse`, `BPPO012` fails unresolved route placeholders, and `BPPO013` warns about nullable route parameters that can never actually be omitted.

### HTTP and link diagnostics
HTTP-shape conflicts are handled at compile time instead of by endpoint surprises. `BPOA002` reports conflicting HTTP method attributes, `BPLG001` and `BPLG006` validate link attributes and placeholder usage, and `BPLG007` now reports route collisions between different operations that claim the same route+method pair. That route-collision diagnostic aligns with the runtime endpoint-collision warning added during startup registration.

### Validation, result, and serializer diagnostics
The remaining migration-critical diagnostics cluster around operation definitions and serializer setup. `BPV012` warns about meaningless `[Required]` usage on non-nullable value types, `BPV013` highlights `IValidatableObject` participation, `BP001` / `BP002` / `BP005` / `BP104` / `BP108` cover invalid pipeline and result declarations, `BPP001` guards `[DoNotSaveChanges]`, `BP006` enforces serializer-context coverage for problem details, `BP009` catches duplicate descriptor names after suffix stripping, and `BP010` rejects unevaluable `ApiExceptionFactory` initializers.

### `ApiExceptionFactory` moved to compile time
The `ApiExceptionFactory` work deserves to stay visible because it is a concrete migration win. `ApiExceptionFactoryModule` now scans operation fields at compile time, extracts status-code metadata from enum/int/cast initializers, projects those responses into OpenAPI, and raises `BP010` when it cannot evaluate an initializer safely. That replaces the old runtime reflection convention and directly benefits the handful of S365 operations that use `ApiExceptionFactory` patterns.

### Code fixes for migration-heavy diagnostics
The diagnostic story is backed by actual IDE help. `Blueprint.SourceGenerator.CodeFixes` ships an `AddPartialModifierCodeFixProvider` for `BP001` / `BP002` / `BP005` / `BP104`, supports Fix All in Solution, and was split into its own project to satisfy RS1038. That matters for large migrations because “make 240+ operation types partial” is realistic work only if the code fix can do it at solution scale.

| Diagnostic family | Codes consolidated here |
|---|---|
| Authorization | `BPAU003`, `BPAU004`, `BPAU008` |
| Property ownership | `BPPO006`, `BPPO009`, `BPPO010`, `BPPO011`, `BPPO012`, `BPPO013` |
| HTTP / links | `BPOA002`, `BPLG001`, `BPLG006`, `BPLG007` |
| Validation | `BPV012`, `BPV013` |
| Result / serializer / naming | `BP001`, `BP002`, `BP005`, `BP006`, `BP009`, `BP010`, `BP104`, `BP108`, `BPP001` |

### Diagnostic philosophy after consolidation
The best way to read the diagnostics section is that Blueprint now prefers compile-time proof over runtime repair. Invalid templates, bad route placeholders, duplicate names, unsupported ownership combinations, and missing serializer metadata now fail or warn close to the source rather than surfacing only after a service boots or a frontend consumes the wrong document.

| Common migration mistake | Diagnostic family that catches it |
|---|---|
| Bad `{Auth:...}` or `{Property}` token | `BPAU004` |
| Resource auth on the wrong operation kind | `BPAU008` |
| Mixed property ownership sources | `BPPO006` / `BPPO010` |
| Route placeholder mismatch | `BPPO012` / `BPLG001` |
| TypeConverter-only binding path | `BPPO011` |
| Duplicate operation names after suffix stripping | `BP009` |
| Missing problem-details serializer metadata | `BP006` |
| Invalid `ApiExceptionFactory` initializer | `BP010` |
| Route/method collisions | `BPLG007` |

## Codemod Results

### S365 codemod run summary
The codemod was run against `Signature365.sln` (158 projects) and found 10 `BPMIG` diagnostics across four buckets. The duplicates in the old document can be replaced with the simple summary below: some items are pure deletion, some require startup/config rewrites, some require consumer extension updates, and a few remain deliberate design-review checkpoints.

| Bucket | IDs | Consolidated action |
|--------|-----|---------------------|
| Auto-fixable | `BPMIG001`, `BPMIG002`, `BPMIG003`, `BPMIG400`, `BPMIG401` | Remove redundant setup and add `partial` where required |
| Configuration required | `BPMIG100`, `BPMIG101` | Move legacy `AddOpenApi(...)` / `AddAuthentication(...)` configuration to the new seams |
| Consumer-extension rewrite | `BPMIG200`, `BPMIG201` | Update `FromResource(...)` usage and verify `{Auth:...}` template handling |
| Manual design review | `BPMIG300`, `BPMIG301`, `BPMIG302` | Namespace cleanup, middleware-to-module review, and startup rewrite |

| BPMIG code | Practical meaning |
|---|---|
| `BPMIG001` | Remove redundant `AddOpenApi()` |
| `BPMIG002` | Remove redundant `AddHateoasLinks()` |
| `BPMIG003` | Remove redundant `AddAuthorisation()` |
| `BPMIG100` | Move configured OpenAPI customization to `MapBlueprintApi(...)` |
| `BPMIG101` | Move configured authentication/context-loader setup to the new seam |
| `BPMIG200` | Migrate legacy `FromResource(...)` forms |
| `BPMIG201` | Verify `{Auth:...}` placeholder handling |
| `BPMIG300` | Update legacy Blueprint namespaces |
| `BPMIG301` | Review `IMiddlewareBuilder` usage and replace with modules or ASP.NET middleware |
| `BPMIG302` | Replace legacy `BlueprintApiBuilder` startup flow |
| `BPMIG400` | Add `partial` to operation types |
| `BPMIG401` | Add `partial` to API-resource types |

### Auto-fix and review details
The high-volume mechanical changes are well understood. `BPMIG400` adds `partial` to ~240+ operation types, `BPMIG401` does the same for ~60 resource types, and the legacy activation cleanup removes redundant `AddOpenApi()`, `AddHateoasLinks()`, and `AddAuthorisation()` calls because those features are now implied by the generated app model. `BPMIG202` is no longer a blocker at all because abstract generic base operations are already supported; it was downgraded to an informational path.

### Code-fix coverage in the codemod toolchain
The codemod story is not just “report and stop.” `LegacyActivationCodeFixProvider` handles the redundant-activation cleanups, `MissingPartialCodeFixProvider` covers both partial-class diagnostics, and the separate Blueprint code-fix project covers the related BP* diagnostics inside the IDE. Together, that means the migration’s highest-volume edits are mostly mechanical and tool-assisted rather than handwritten.

### Tooling issues found and fixed during the S365 run
Two codemod/tooling failures were worth preserving because they explain why earlier session notes were noisy. First, SARIF output originally concatenated because all projects wrote to the same file; that was fixed with per-project SARIF paths driven by `$(MSBuildProjectName)`. Second, the codemod originally failed the entire analysis when the S365 build had unrelated CA*/SA*/CS* warnings; it now reports `BPMIG` findings even when non-migration diagnostics are present and only throws when the build fails with zero migration findings.

### Proof-project and migration mechanics
The proof project remains useful because it compresses the real migration mechanics into one place. The successful compile proved the required shape: operations and resources become `partial`, services provide a `BlueprintApp` plus `[BlueprintAppScan]`, apps add a serializer context with `ProblemDetails` / `ValidationProblemDetails`, and startup wiring moves from fluent runtime registration to explicit app/executor registration plus per-service OpenAPI configuration. The proof currently emits 46 generated files.

### What the codemod does not try to own
A few items were intentionally left outside codemod automation. Service-specific JWT context loaders, S365 resource-key expansion, custom middleware that is really application middleware, custom typed-client generators, and operational concerns such as health checks or GraphProxy hosting are all still consumer decisions. The migration is therefore mostly mechanical, but it is not meant to erase every S365-specific seam.

| Mechanical migration checklist | Why it stays explicit |
|---|---|
| Add `partial` to operation and resource types | Required for generation; codemod/code fixes can do most of it |
| Create `BlueprintApp` + `[BlueprintAppScan]` | Replaces runtime operation scanning |
| Add serializer context with problem-details types | Required for STJ + OpenAPI correctness |
| Rewrite startup registration | The largest non-mechanical service-level change |
| Re-register auth / tenant / Marten / Rebus / Hangfire seams | Consumer-specific DI still matters |
| Run OpenAPI + TypeScript diff | Final compatibility gate per service |

### Migration posture after codemod consolidation
The codemod findings support the same overall conclusion as the rest of this document: migration is mostly mechanical, but it still needs deliberate service-level review at the seams where S365 intentionally owns behavior—auth context loading, service startup composition, infrastructure registration, and frontend OpenAPI consumption.

| Delivered codemod support | Status |
|---|---|
| Per-project SARIF output | Implemented |
| Lenient build-failure handling when BPMIG diagnostics exist | Implemented |
| Redundant activation cleanup | Implemented |
| Missing `partial` code fixes | Implemented |
| Abstract generic base false-positive removal (`BPMIG202`) | Resolved |

## Open Questions

### Resolved questions
All of the historical open questions in the original document are now resolved, so they can stay as a short checklist instead of a running log:

1. OpenAPI endpoint path → use RFC 9264 `/.well-known/openapi`.
2. Pipeline IL duplication → major duplication is already extracted into `PipelineHelpers`.
3. Tenant resolution ordering → tenant resolution belongs in the generated pipeline, not custom runtime middleware.
4. Property-populator elimination → yes; `TenantModule` + compile-time ownership rules replace the meaningful S365 populator behavior.
5. `{Auth:...}` template expansion → supported through compile-time template parsing and `FromResource` overrides.
6. Handlebars template compatibility → the required OpenAPI features are known and emitted.
7. `StateBasedLinkChecker` → implemented with the same API shape.
8. Inline `InvokeAsync` convention → supported alongside `Invoke`, `Handle`, and `HandleAsync`.
9. Proxy result handling → `ProxyResult` is now a first-class `HttpResult` subtype.
10. Exception translation ordering → Blueprint preserves ordered, overlap-checked exception policy resolution.
11. `Permission` / `PermissionFor` cleanup → not applicable; source-gen Blueprint is already resource-only.
12. Custom middleware blockers → all confirmed S365 middleware patterns now map to modules or ordinary ASP.NET Core middleware.

### Remaining hard gates and watch items
The migration is ready, but a few items still deserve explicit verification during each real service move:

- **Per-service TypeScript verification**: generate the new OpenAPI document, run the existing Handlebars generator unchanged, and diff `api.ts`.
- **Status-code compatibility flag**: enable `UseStatus200ForSuccessResponses` anywhere a frontend still assumes `responses["200"]` is the only success schema.
- **Error content-type compatibility**: enable `UsePlainJsonForErrorResponses` when older clients assume `application/json` rather than `application/problem+json`.
- **Query-string casing**: legacy PascalCase query parameters become camelCase in the new client path; hand-built URLs must be updated.
- **Serilog tenant enrichment**: keep it as standard ASP.NET Core middleware; it is not a `TenantModule` responsibility.
- **`ApiId` performance**: add `TryParse` / `IParsable<ApiId>` if the info-level TypeConverter fallback warning becomes noise or invalid-input cost matters.

### Recommended migration order
The most stable migration order is now clear from the consolidated findings: (1) make operations/resources `partial`; (2) add `BlueprintApp` and serializer context; (3) rewrite startup registration and tenant/auth DI; (4) verify OpenAPI output and TypeScript generation; (5) swap service-to-service clients and Rebus/Hangfire registrations; (6) only then trim any remaining S365-owned compatibility layers that are no longer useful.
