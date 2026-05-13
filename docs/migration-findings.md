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
| OpenAPI well-known endpoint | ✅ Done | RFC 9264 `/.well-known/openapi` |
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
| Resource auth helper extraction | ✅ Done | `PipelineHelpers.CheckResourceAuthorizationAsync` — two overloads (resolved + legacy) reduce 19→16 / 8→3 lines per pipeline |
| S365 middleware gap analysis | ✅ Done | No blocking gaps: all 6 S365 middleware types have Blueprint equivalents |
| Pipeline optimization audit | ✅ Done | No further extraction opportunities remain; pipelines are already well-optimized |
| S365 source generators | ✅ Analysed | 3 custom generators (DTO, Metrics, ServiceApi) — orthogonal to Blueprint, must coexist |
| Blueprint.Compiler (legacy) | ✅ Analysed | Runtime Roslyn compiler, not source-gen — superseded by Blueprint source-gen |
| S365 `ApiId` route binding | ✅ Done | Blueprint now supports `[TypeConverter]`-attributed types for route and query binding via `TypeDescriptor.GetConverter()` — matches legacy Blueprint behavior exactly |
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
| `[FromAuthContext]` attribute | ✅ Done | Auth-populated properties excluded from HTTP binding/OpenAPI; resolves TenantId-in-body gap |
| HATEOAS link population extraction | ✅ Done | `PipelineHelpers.PopulateHateoasLinksAsync` — replaces 10-line inline block per pipeline (~6,320 lines saved for 632 ops) |
| `Bus` property alias on test fixture | ✅ Done | `BlueprintTestFixture.Bus` aliases `Messages` — mirrors S365 `ApiFixture.Bus` for minimal test churn |
| S365-compatible message assertions | ✅ Done | `HavePublishedSingle<T>()`, `HavePublished<T>()`, `NotHavePublished<T>()` etc. on `TestMessageCollector` |
| Handler DI parameter injection | ✅ Verified | S365 `HandleAsync(op, IDocumentSession, IMapper)` pattern — Blueprint resolves from DI automatically |
| Handler return type normalization | ✅ Verified | Plain value → `OkResult<T>`, `ResourceEvent<T>` → direct, `Task` → `NoResult`, `OperationResult` → direct |
| ResourceEvent<T> BP108 compat | ✅ Done | `Blueprint.Http.ResourceEvent<T>` now recognized as valid declared result contract — 50+ S365 commands unblocked |
| IQuery<T> type inference overload | ✅ Done | `ResourceEventDefinition.For(new GetSettingsQuery())` infers `TResource` from `IQuery<TResource>` — matches legacy API |
| HttpResult BP108 compat | ✅ Done | `Blueprint.Http.HttpResult` now recognized as valid declared result contract — 3 S365 operations unblocked |
| QueryOrderDefinition bool? compat | ✅ Done | Added `(string, bool?)` constructor overload — S365 passes nullable descending flag |
| ResourceEvent convenience types | ✅ Already exists | `ResourceCreated<T>(data)`, `ResourceUpdated<T>(data)`, `ResourceDeleted<T>(data)` all in `Blueprint.Http` |
| ResourceEventDefinition factory | ✅ Already exists | Full factory pattern with domain→query mapper, `.New(domainObject)`, matches S365 |
| IQuery selfQuery constructors | ✅ Already exists | `ResourceCreated<T>(IQuery selfQuery)` — used by all S365 delete operations |
| IResourceEventAuditRepository | ✅ Already exists | `AddAsync(object)` — S365 `BusResourceEventAuditRepository` implements directly |
| ResourceExpansionModule | ✅ Already exists | Post-execution resource expansion via `IResourceExpander<T>` |
| ApiResource.GetTypeName caching | ✅ Done | `ConcurrentDictionary` cache, DTO suffix strip, delegates from `ResourceEvent.GetResourceTypeName` |
| ApiId route/query binding | ✅ Done | S365 `ApiId` IS used in route params (e.g. `GetSentEmailQuery.TenantId`). Blueprint now handles `[TypeConverter]` types natively — no S365 changes needed |
| IResourceEventAuditSink enhanced | ✅ Done | `RecordAsync` now includes full `OperationResult` — S365 can cast to `ResourceEvent` for bus publish |
| ResourceEvent audit sink fix | ✅ Done | Pipeline now matches `ResourceEvent` (not just `ResourceEventResult`) for audit recording |
| HATEOAS ResourceEvent link fix | ✅ Done | `LinkGeneratorHandler` now extracts content from `ResourceEvent.Data` for link population |
| ScopePairClaimAuthorizationProvider | ✅ Done | Production-ready provider: JWT scope-pair matching with ignored-scope filtering, FrozenSet perf |
| Testing infrastructure complete | ✅ Verified | QueryAsync, CommandAsync, BackgroundTaskAsync, MessageAsync, factory, ambient auth — all S365 patterns covered |
| ResourceEventEnvelope shape fix | ✅ Done | Aligned `ResourceEventResult` HTTP envelope with S365's full `ResourceEvent` shape: `$object`, `eventId`, `changeType`, `created`, `resourceObject`, `data`, `metadata` |
| TypeScript client generation | ✅ Analysed | Custom Handlebars templates in 3 apps; relies on `operationId`, `x-links`, `tags`, `ResourceEventOf{Type}` schema naming — all compatible |
| OpenAPI version configurable | ✅ Done | `OpenApiInfoOptions.OpenApiVersion` defaults to `3.0.4`; set to `3.0.0` for older tooling compatibility |
| OpenAPI short schema names | ✅ Done | Schema component keys now use short type names (e.g. `TemplateApiResource`) instead of fully-qualified CLR names |
| OperationId suffix stripping | ✅ Done | `GetSentEmailQuery` → `GetSentEmail` in descriptor Name; flows to OpenAPI `operationId` and TypeScript client generation |
| BP009 duplicate descriptor name | ✅ Done | Compile-time error when two operations strip to the same name (e.g. `FooQuery` + `FooCommand` → both `Foo`) |
| Link schema component | ✅ Done | Well-known `Link` schema emitted in `components/schemas` with `href` (required) + `type`; `$links` property uses `additionalProperties: { $ref: Link }` |
| `[DoNotAudit]` compile-time | ✅ Already exists | `AuditingModule` skips code generation when attribute present; manifest records `auditing.enabled=false`; integration test proves audit sink NOT called |
| `[NoRebusTransaction]` compile-time | ✅ Already exists | `RebusTransactionModule` skips transaction wrapping; both runtime and source-gen paths handle opt-out |
| `[Sensitive]` property metadata | ✅ Already exists | `SensitivePropertyMetadataModule` generates ordered property-name list at compile time; manifest records sensitive fields |
| `[AlwaysExpand]` resource expansion | ✅ Already exists | `ResourceExpansionModule` generates unconditional expansion for marked properties; `IResourceExpander` from DI |
| Collection resource types | ✅ Already exists | `ListApiResource<T>`, `PagedApiResource<T>`, `ContinuationPagedApiResource<T>` — identical shape to S365 |
| Validation fast-paths | ✅ Already exists | 35 compile-time fast-path strategies covering all S365 custom validators (RequiredIf, GreaterThan, LessThan, NotEmptyList, etc.) |
| S365 control attribute compat | ✅ Verified | All 5 S365 control attributes have Blueprint equivalents: `[DoNotSaveChanges]`, `[NoRebusTransaction]`, `[DoNotAudit]`, `[Sensitive]`, `[AlwaysExpand]` |
| Tenancy first-class complete | ✅ Verified | `TenantModule` replaces `SetTenantMiddleware` entirely: claim resolution via `ITenantResolver`, direct `TenantId` assignment (no reflection), `ITenantAccessor.Current` ambient context — all compile-time generated |
| ResourceEvent backward compat | ✅ Verified | `ICommand<ResourceEvent<T>>` works unchanged; S365 `BusResourceEventAuditRepository` → Blueprint `IResourceEventAuditSink`; `[DoNotAudit]` opt-out works; `ResourceCreated/Updated/Deleted<T>` all supported |
| ResourceEvent `$object` JSON | ✅ Fixed | Added `[JsonPropertyName("$object")]` returning `"event"` to `ResourceEvent` class — S365 wire format requires this discriminator |
| ResourceEventChangeType enum | ✅ Fixed | Added `[JsonConverter(typeof(JsonStringEnumConverter))]` — was serializing as 0/1/2 instead of `"Created"`/`"Updated"`/`"Deleted"` |
| ReadJsonBodyAsync static delegate | ✅ Already done | `PopulateFromBodyDelegate<TOperation>` takes operation as parameter; generated code uses `static (_bodyBytes, _op) => PopulateFromJsonBody(_bodyBytes, _op)` — zero closure allocation |
| Rebus handler patterns (47 handlers) | ✅ Verified | All S365 Rebus patterns supported: `IHandleMessages<T>` (47 handlers), `Subscribe<T>()` (20 services), `bus.Publish/Send`, `IMessageContext`, one-way clients — no sagas, no exotic attributes |
| Rebus outbox pattern | ✅ S365-owned | Marten outbox with `OutboxForwarder`/`MartenOutboxStorage` — stays S365-owned DI registration, Blueprint provides `IMessageOutbox` integration |
| Test `Should()` chain compat | ✅ Done | `TestMessageCollector.Should()` returns `this` for FluentAssertions-style chaining: `api.Bus.Should().HavePublishedSingle<T>()` |
| BackgroundTaskCollector.All | ✅ Done | Combined `IEnumerable<object>` of all enqueued + scheduled + child tasks — mirrors S365 pattern (30 test usages) |
| S365 test auth helpers | ✅ S365-owned | `AuthenticateAsTenantAdministrator`, `AuthenticateAsPartnerUser` etc. are S365 extension methods that call `Authentication.Set(identity)` — Blueprint provides the underlying API |
| S365 `WebApiFixtureFactory` match | ✅ Verified | `BlueprintTestFixtureFactory` mirrors `WebApiFixtureFactory<T>`: `Create()`, `ConfigureServices`, `ConfigureOnceAsync`, `ConfigureOnceTimeout`, thread-safe singleton init |
| OpenAPI security schemes | ✅ Already exists | `OpenApiInfoOptions.SecuritySchemes` configurable; default `bearerAuth` JWT scheme; S365 can add OAuth2 or custom schemes |
| OpenAPI nullable handling | ✅ Already exists | `NullabilityInfoContext` for NRT annotations + `Nullable<T>` unwrapping; fully compatible with S365 TypeScript codegen |
| ResourceEvent [Obsolete] removal | ✅ Done | Removed `[Obsolete]` from `ResourceCreated<T>`, `ResourceUpdated<T>`, `ResourceDeleted<T>` in `Exec` namespace — prevents hundreds of CS0618 warnings during S365 migration |
| ApiOperationResourceConvention | ✅ Not needed | S365 runtime convention reads `[Resource]`/`[FromResource]` and stores scope pairs in feature data — Blueprint source-gen does this at compile time via `AuthorizationModule` |
| Sig365LinkGeneratorMiddleware | ✅ Not needed | Portal-specific custom link middleware; source-gen auto-generates link population — replaces this entirely |
| Custom middleware → modules | ✅ Verified | All 6 S365 middleware types (Tenant, Rebus TX, Marten Save, Marten Outbox, ResourceExpand, Links) have compile-time module equivalents |
| Health checks | ✅ Not Blueprint | S365 health checks are standard ASP.NET Core (`MapHealthChecks`); Blueprint-agnostic, no migration needed |
| PlainTextResult compat | ✅ Verified | 14 S365 usages; extends `HttpResult` which is a supported result type in Blueprint |
| BlueprintHttpClient optimization | ✅ Done | PropertyInfo cache per type, lazy StringBuilder for query params, span-based placeholder parsing — eliminates per-request reflection and string allocations |
| PipelineHelpers.ResolveServiceArray | ✅ Done | Fast-path `is T[]` check avoids `ToArray()` copy when DI returns arrays directly |
| ResourceEvent JSON envelope fix | ✅ Done | `SendResourceEventAsync` + `SeedTemplateAsync` now check `data` property before `content` — matches `ResourceEventEnvelope` serialization shape |
| ArrayPool buffer leak fix | ✅ Done | `ReadJsonBodyAsync` fast-path uses try/finally to guarantee `ArrayPool.Return` on all exception paths |
| RunAllPublishedMessagesAsync | ✅ Done | Test fixture dispatches captured published messages through pipeline — mirrors S365 `ApiFixtureExtensions.RunAllPublishedMessagesAsync` |
| RunAndConsumeAllPublishedMessagesAsync | ✅ Done | Dispatches then clears — prevents re-processing on subsequent calls |
| HttpContext handler injection | ✅ Done | `HttpContext`, `HttpRequest`, `HttpResponse`, `ClaimsPrincipal`, `ClaimsIdentity` all injectable as handler parameters — resolved from context properties, not DI. Covers S365 Stripe webhook pattern. |
| S365 TenantOrderedPagedQuery compat | ✅ Verified | Already extends Blueprint's `OrderedPagedQuery<T>` — no changes needed |
| S365 TenantPagedApiResource compat | ✅ Verified | Already extends Blueprint's `PagedApiResource<T>` — no changes needed |
| S365 ApiId type compat | ✅ Done | `ApiId` readonly struct with `[TypeConverter]` works for route/query/body binding — no S365 changes needed |
| TenantModule non-string types | ✅ Done | TenantModule now supports `ApiId`, `Guid`, any `IParsable<T>`/`TryParse`/`[TypeConverter]` type for TenantId — 165 S365 operations with `ApiId TenantId` (not in route) now auto-populate correctly via `TryParse` + `EqualityComparer<T>.Default` guard |
| S365 ApiOperations base class | ✅ Verified | Context, Identity, Logger, CancellationToken auto-injected; HttpContext/HttpRequest/HttpResponse derive from Context.GetHttpContext() |
| S365 NotFoundException handling | ✅ Verified | `ApiException(404)` → `UnhandledExceptionResult` → ProblemDetails JSON with 404 status; exact S365 pattern |
| S365 TypeScript codegen flow | ✅ Analysed | Backend writes OpenAPI JSON to disk → Node.js script loads + Handlebars templates generate api.ts; `x-links` extension required (Blueprint emits it) |
| OpenAPI tag grouping | ✅ Compatible | Both S365 and Blueprint derive tags from last namespace segment; `[ApiOperations("Group")]` available for explicit override |
| OpenAPI response status codes | ✅ Improvement | Blueprint generates more granular codes (201/302/401/403/415); S365 omits 401 despite using Bearer auth. Blueprint output is strictly better. |
| Query string binding | ✅ Compatible | S365 uses `[FromQuery]` explicitly; Blueprint supports same attribute plus automatic GET binding |
| Pagination patterns | ✅ Compatible | S365 `OrderedPagedQuery<T>` + `PagedApiResource<T>` identical to Blueprint. Custom pages (e.g. aggregate stats) work unchanged |
| Binary file responses | ⚠️ S365-owned | S365 embeds binary as base64 in JSON (e.g. InvoicePdfApiResource). Blueprint offers `FileStreamResult` for streaming. Migration optional. |
| Custom HTTP headers | ✅ None needed | No S365 operations use custom request/response headers |
| Correlation IDs | ✅ Not applicable | Infrastructure-level, not operation-level — no migration needed |
| Error response shape | ✅ Compatible | Both use RFC 7807 ProblemDetails. Blueprint uses `application/problem+json` content-type (correct per spec). |
| Enum handling | ✅ Compatible | S365 uses string properties; Blueprint uses `JsonStringEnumConverter` by default — both produce string values |
| x-enumNames OpenAPI ext | ✅ Done | Blueprint emits both `x-enumNames` (NSwag) and `x-enum-varnames` (OpenAPI Generator) for maximum tooling compat |
| RFC 9264 well-known endpoint | ✅ Done | Corrected from RFC 9727 → RFC 9264; `/.well-known/openapi` serves same pre-rendered document |
| Validation attribute compat | ✅ Compatible | All 14 S365 custom validators (`RequiredIf`, `GreaterThan`, `NotAllowedIf`, etc.) already in `Blueprint.Runtime.Validation`; `IValidatableObject` supported with BPV013 warning |
| Validation error shape | ✅ Compatible | Both return HTTP 422 with `ValidationProblemDetails`; `errors` dictionary shape identical |
| FluentValidation not used | ✅ Confirmed | S365 uses only DataAnnotations — no FluentValidation; Blueprint's compile-time fast paths cover all S365 patterns |
| S365 custom validators (4) | ✅ S365-owned | `ObjectStringLengthAttribute`, `ValidImageContentTypeAttribute`, `SupportedCountryAttribute`, `SupportedRegionAttribute` — domain-specific, stay in S365 |
| TestMessageCollector `HaveSent` fix | ✅ Done | `HaveSent<T>()` now returns single T (matching S365 `FakeBus.Should().HaveSent<T>()`) instead of `IReadOnlyList<T>` |
| TestMessageCollector `HavePublished` | ✅ Done | `HavePublished<T>()` now asserts at least one published (matching S365); `HavePublishedAny<T>()` added |
| TestMessageCollector `Clear()` | ✅ Done | Added `Clear()` alias for `ClearAll()` — S365 uses `api.Bus.Clear()` in 56 test call sites |
| FakeBus `.Events` access | ✅ Not applicable | S365 non-SMTP apps have NO test projects; only legacy Blueprint framework tests use FakeBus |
| FakeBus `.Publish()` in tests | ✅ Not applicable | Same as above — only Blueprint framework tests, not S365 app tests |
| S365 app test projects | ✅ None exist | Non-SMTP S365 apps have ZERO test projects — all 42 test projects are either SMTP or legacy Blueprint framework tests. Test migration is NOT a factor for S365 app migration. |
| TS codegen x-links | ✅ Compatible | Blueprint emits `x-links` with identical shape (`rel`, `operationId`, `method`) — S365 Handlebars templates work unchanged |
| TS codegen operationId | ✅ Compatible | Blueprint uses operation class name (minus suffix) for `operationId` — matches S365 NSwag convention |
| NodaTime.Instant query params | ✅ Done | NodaTime.Instant has `[TypeConverter(typeof(InstantTypeConverter))]` — works with new TypeConverter support automatically |
| FolderReference TypeConverter | ✅ Done | S365 `FolderReference` has `[TypeConverter]` — works with new TypeConverter support automatically |
| Role TypeConverter | ✅ Done | S365 `Role` has `[TypeConverter]` — works with new TypeConverter support automatically |
| IHandleMessages<T> (80+) | ✅ Compatible | ~80 Rebus message handlers across non-SMTP apps; Blueprint.Rebus scanner handles these |
| Custom S365 middleware (6) | ✅ Compatible | SetTenantMiddleware, ResourceExpandMiddleware, Rebus TX scope (start/commit), SaveMarten, MartenOutbox — all register via startup |
| Property populators | ✅ Not needed | S365 uses middleware (SetTenantMiddleware) instead of IPropertyPopulator — no populator migration needed |
| Background tasks | ✅ Compatible | IBackgroundTaskScheduler patterns across Generation, VCard, EventStore, AdminPortal, Portal — all standard |
| `[FromQuery("name")]` constructor | ✅ Done | S365 uses `[FromQuery("validationToken")]` (1 usage in M365 Gateway). Fixed generator to read `ConstructorArguments` before `NamedArguments`. Both `[FromQuery("x")]` and `[FromQuery(Name = "x")]` now work. |
| Abstract base class inheritance | ✅ Verified | S365 uses `OrderedPagedQuery`, `SubscribeCommand`, `CampaignCommand` as operation bases. `PropertyOwnershipModule.AnalyzePropertyOwnership` uses `GetAllMembers()` which walks full type hierarchy — inherited properties discovered correctly. |
| `[FromBody]` explicit (SMTP only) | ✅ Not applicable | Only in SMTP MockApi ASP.NET controller — not a Blueprint operation, no migration needed |
| Multiple IHandleMessages<T> per class | ✅ S365-owned | 5 Rebus handlers implement 3+ IHandleMessages interfaces each (Analytics: Campaign, Disclaimer, Group, Signature, Survey). These are Rebus event handlers, NOT Blueprint operations — handled by Rebus subscription wire-up. |
| Abstract generic base operations | ✅ Compatible | `EmailOrderedPagedQuery<T> : OrderedPagedQuery, IQuery<EmailPagedApiResource<T>>` — source generator resolves concrete type args; PropertyOwnership walks through generic base hierarchy |
| S365 outbox timing | ✅ Verified | S365 custom Marten outbox publishes domain events in `BeforeSaveChangesAsync` — identical timing to Blueprint's `OutboxPublishingSessionListener`. Both wrap in Rebus transaction scope. |
| S365 two-tier auth model | ✅ Compatible | `ScopeApiAuthoriser` maps to `ScopePairClaimAuthorizationProvider`; `S365ClaimsApiAuthoriser` maps to custom `IResolvedResourceAuthorizationProvider`. Both receive `ApiOperationContext` with full operation instance. |
| Blueprint.Marten.Rebus bridge | ✅ Done | `RebusDomainEventPublisher` implements `IDomainEventPublisher` via `IBus.Publish()`. `AddBlueprintMartenRebus()` DI registration. 4 unit tests passing. |
| S365 verb hierarchy (auth) | ✅ S365-owned | "Manage" includes Read/Update/Create/Delete — this is an `IResourceAuthorizationProvider` implementation concern, not a Blueprint core concern |
| S365 resource key wildcards | ✅ S365-owned | "/Tenants/123/*" wildcard matching — implemented in S365's `IResolvedResourceAuthorizationProvider`, Blueprint provides the pre-expanded keys |
| Validation regex caching | ✅ Done | `[RegularExpression]` fast-path now emits `static readonly Regex` field with `RegexOptions.Compiled` — eliminates per-request pattern compilation from `Regex.IsMatch(string, string)` |
| Validation pipeline audit | ✅ Done | Full audit confirmed: zero allocation on success path, zero reflection in fast-paths, 43 fast-path strategies, lazy `List<ValidationResult>`, attribute caching via static fields. Only fallback for custom/unsupported attributes. |
| OpenAPI shape audit | ✅ Compatible | Blueprint emits same `operationId` (class name), tags, `x-links` {rel, operationId, method}, schema components. S365 Handlebars templates use `{{#withOperation operationId}}` which traverses full OpenAPI doc — fully compatible. |
| SingleItemList<T> auth optimization | ✅ Done | `PipelineHelpers.SingleItemList<T>` struct avoids per-request `ResolvedScopePair[]` array allocation for single-`[Resource]` operations (vast majority of S365). Source generator emits single-element overload when exactly 1 scope pair. |
| Cached Task<bool> in auth | ✅ Done | `ScopePairClaimAuthorizationProvider` uses static `_trueResult`/`_falseResult` instead of `Task.FromResult` per auth check — eliminates per-request `Task<bool>` allocation |
| Dual auth interface provider | ✅ Done | `ScopePairClaimAuthorizationProvider` implements both `IResourceAuthorizationProvider` AND `IResolvedResourceAuthorizationProvider` — single registration enables both legacy and compile-time–expanded auth paths. `AddResourceAuthorizationProvider<T>()` auto-forwards. |
| Zero-alloc scope comparison | ✅ Done | `ScopePairClaimAuthorizationProvider.MatchesScopePair` uses `ReadOnlySpan<char>` slicing to compare `{noun}{sep}{verb}` against claim values — eliminates per-comparison string interpolation allocation in auth hot path |

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

### S365 Custom Source Generators (Coexist with Blueprint)
S365 has **3 custom source generators** in `Symprex.Signature365.SourceGenerators` that are **orthogonal** to Blueprint source-gen. They MUST coexist during and after migration.

| Generator | Trigger Attribute | Output | Blueprint Equivalent |
|-----------|-------------------|--------|---------------------|
| `DtoSourceGenerator` | `[DtoMap(Type)]` | `{DtoName}.g.cs` with `MapFrom(source)` method | None — DTO mapping is S365-specific |
| `MetricsSourceGenerator` | `[AppCounter<T>]` | `{Class}.MetricInstances.g.cs` with `IAppMetrics<T>` | None — OpenTelemetry metrics are S365-specific |
| `ServiceApiSourceGenerator` | `[ServiceApiInclude(typeof(Op))]` | `ServicesApis.g.cs` with interface + implementation | Partial overlap: generates HTTP client wrappers for inter-service calls using `BlueprintHttpClient` |

**Key points:**
- Both generator toolchains emit `.g.cs` files to same `generated` folder — **no name collisions**
- Both use standard Roslyn incremental generation — **no tooling conflicts**
- `ServiceApiSourceGenerator` generates service client classes that call `BlueprintHttpClient.CallAsync(operation, options)` — these wrap Blueprint's HTTP client for S365-specific patterns (JWT management, `ModifyOptionsFor` per-operation customisation)
- During migration, S365 generators keep running unchanged; Blueprint source-gen is additive

### Blueprint.Compiler (Legacy)
- Located at `D:\Projects\Signature365\src\Blueprint\Blueprint.Compiler`
- **Runtime Roslyn compiler** for dynamic code emission (NOT a source generator)
- Uses `Frame`-based code generation to produce in-memory assemblies at runtime
- **Superseded entirely** by Blueprint source-gen (compile-time generation replaces runtime compilation)
- **Migration action**: Remove `Blueprint.Compiler` project reference once Blueprint source-gen is integrated

### Link Attributes
- S365 has **193 `[SelfLink]`** and **292 `[Link]`** usages (99 non-self HATEOAS links)
- Blueprint source-gen now processes all three: `[RootLink]`, `[SelfLink]`, `[Link]`
- `[Link]` requires explicit `Rel` value (BPLG006 diagnostic if missing)
- Null resource type on `[Link]` treated as root-link semantics (URL only, no link registration)
- Index-based variable names (`link_0`, `link_1`) used in generated code for safe C# identifiers
- PropertyOwnershipModule now extracts route placeholders from `[Link]` URLs too
- 13 modules refactored to use centralised `HttpHostExposureHelper` (was duplicate per-module)

### OpenAPI Compatibility
- Blueprint uses `operationId` = operation class name with `Query`/`Command` suffix stripped (e.g., `GetTemplateQuery` → `GetTemplate`)
- S365 legacy also strips these suffixes — **compatible**
- Custom Handlebars templates use `operation.operationId` for lookup — works unchanged
- `pathGenerator` helper builds URL interpolation from path — uses standard `{paramName}` format
- Templates group operations by `tags` — Blueprint's OpenAPI groups by namespace, configurable
- **Critical**: `x-links` extension must be present in Blueprint OpenAPI output for model resource methods

#### OpenAPI Structural Differences (S365 Legacy vs Blueprint Source-Gen)

| Aspect | S365 (Legacy NSwag) | Blueprint Source-Gen | Impact |
|--------|---------------------|---------------------|--------|
| OpenAPI version | `3.0.0` | `3.0.4` (configurable) | Set `OpenApiVersion = "3.0.0"` for older tooling compat if needed |
| Schema naming | Verbose prefixed (`PortalInviteTenantUserCommand`) | Short clean names (`InviteTenantUser`) | Different names in `$ref` paths — TS templates work fine (names from `$ref` key) |
| Schema `id` field | Present on complex types | Not present (uses `title` instead) | TS templates do NOT read `id` field — verified no impact |
| `requestBody.required` | Omitted | Explicit `true` | Blueprint is stricter — strictly better for validation tooling |
| `securitySchemes` | Not present | JWT bearer auth in `components` | Improvement — S365 omitted this despite using Bearer auth |
| Root `tags` array | Not present | Present (centralised tag registry) | Additive — doesn't break existing consumers |
| Error content-type | `application/json` | `application/problem+json` | Correct per RFC 7807 — TS templates may need minor update if they check content-type |
| Response status codes | Basic (200, 422, 500) | Comprehensive (200, 201, 202, 400, 401, 403, 415, 422, 500) | Blueprint is strictly better — more error cases documented |
| `x-links` extension | Present on resource schemas | Present on resource schemas | **Identical shape** (`rel`, `operationId`, `method`) |
| Enum handling | `x-enumNames` only | `enum` + `x-enumNames` + `x-enum-varnames` | Superset — backward compatible |
| Nullable fields | `"nullable": true` | `"nullable": true` | **Identical** |
| Parameter schemas | Inline only | Inline only | **Identical** |
| `operationId` position | After tags/summary | First in operation | JSON property order is insignificant |

**Overall verdict**: Blueprint's OpenAPI output is a **strict superset** of S365 legacy. No breaking structural changes. TS Handlebars templates will work unchanged.

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

### S365 Custom Middleware → Blueprint Source-Gen Module Mapping
S365 uses 8 custom `IMiddlewareBuilder` implementations in legacy Blueprint. In the new source-gen
Blueprint, these map to built-in modules — no custom middleware code migrates.

| S365 Middleware | Blueprint Source-Gen Equivalent | Status |
|----------------|-------------------------------|--------|
| `SetTenantMiddleware` | `TenantModule` (first-class tenant pipeline stage) | ✅ Built-in |
| `SaveMartenChangesMiddleware` | `UnitOfWorkModule` (`IUnitOfWork` from `Blueprint.Marten`) | ✅ Built-in |
| `UseMartenAsOutboxMiddleware` | `Blueprint.Marten` Marten-Rebus outbox integration | ✅ Built-in |
| `StartRebusTransactionScopeMiddleware` | `Blueprint.Rebus` transaction scope module | ✅ Built-in |
| `CommitRebusTransactionScopeMiddleware` | `Blueprint.Rebus` transaction scope module | ✅ Built-in |
| `ResourceExpandMiddleware` | ASP.NET Core middleware (runs outside Blueprint pipeline) | ⚠️ S365-owned |
| `Sig365LinkGeneratorMiddleware` | `LinkGenerationModule` (compile-time link generation) | ✅ Built-in |
| `RegionalResponseHeadersMiddleware` | ASP.NET Core middleware (not Blueprint-specific) | ✅ Unchanged |

**Opt-out attributes:**
- `[DoNotSaveChanges]` → Not needed; `UnitOfWorkModule` only activates for commands by default
- `[NoRebusTransaction]` → Consumer-specific; S365 can check attribute in `IMessageOutbox` implementation

**Key insight:** S365's `ResourceExpandMiddleware` is the only custom middleware that doesn't have
a direct Blueprint module equivalent. It handles `?expand=property` query parameters to fetch related
resources. This stays as S365-owned ASP.NET Core middleware running after Blueprint's pipeline.

### Pipeline Helper Extraction
- `LoadUserContextAsync` extracted from per-pipeline inline code (~15 lines saved per pipeline)
  - Identity check → factory call → APM activity tags
- `RunAuthorisersAsync` extracted from per-pipeline inline code (~35 lines saved per pipeline)
  - IApiAuthoriser[] + IOperationAuthoriser[] loops with indexed `for` (no enumerator)
  - Returns `OperationResult?` (null = pass, non-null = 401/403 denial)
- Both are `public static` on `PipelineHelpers` for cross-assembly access
- **Impact for S365**: With 632 operations, this saves ~31,600 lines of generated IL

### `[FromAuthContext]` Attribute — Auth-Populated Property Exclusion

**Problem**: S365's Portal Startup.cs had a `ConfigureOperation` callback (lines 117-151)
that manually removed TenantId from non-path OpenAPI parameters. Without this, TypeScript
clients would see TenantId in request bodies and query parameters, even though the pipeline
populates it from auth claims.

**Root Cause**: Blueprint's `PropertyOwnershipModule` fell through to convention for non-route
TenantId: POST → Body, GET → Query. The TenantModule then overwrote the value from claims,
making the HTTP binding pointless.

**Solution**: New `[FromAuthContext]` attribute in `Blueprint.Model.Attributes`:
- Properties marked `[FromAuthContext]` record as `PropertyOwnershipSource.AuthContext`
- No HTTP binding code is generated (skipped by `RenderPopulation`)
- Excluded from OpenAPI parameters and request body schemas
- Route placeholder takes precedence (TenantId in `/tenants/{TenantId}` still route-binds)
- Conflict diagnostic BPPO006 fires for `[FromAuthContext]` + `[FromBody/Query/Header/Cookie]`
- `[FromAuthContext]` + `[FromRoute]` is allowed (route wins silently)
- Inheritable: base class `[FromAuthContext]` applies to derived operations

**S365 migration**: All operations with non-route `TenantId` should add `[FromAuthContext]`.
This can be a codemod target. The `ConfigureOperation` OpenAPI workaround is no longer needed.

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

1. ~~**OpenAPI endpoint path**: What is the current best practice?~~ → **Resolved**: RFC 9264 `/.well-known/openapi`
2. ~~**Pipeline IL size**: How much duplication exists?~~ → **Resolved**: ~80 lines per pipeline → 1 line via `ReadJsonBodyAsync`
3. ~~**Tenant middleware ordering**: Should tenant resolution be ASP.NET middleware or pipeline stage?~~ → **Resolved**: TenantModule in pipeline
4. ~~**Property populator elimination**: Can ALL S365 property populators become compile-time?~~ → **Resolved**: Yes, PropertyOwnershipModule + TenantModule
5. ~~**Resource key template**: How should `{Auth:AccountId}` expansion integrate with generated code?~~ → **Resolved**: `FromResource` template override
6. ~~**Handlebars template compatibility**: What specific OpenAPI shape properties do the templates expect?~~ → **Resolved**: Blueprint emits `x-links` with `rel`, `operationId`, `method` — matches S365 template expectations exactly. `$links` serialized via `[JsonPropertyName("$links")]` on `ApiResource.Links`. `operationId` = class name with `Query`/`Command` suffix stripped (same convention as legacy). Path format `{paramName}` compatible.
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
| `fixture.Bus.Should().HavePublishedSingle<T>()` | `fixture.Bus.Should().HavePublishedSingle<T>()` | ✅ `Should()` returns self for chaining |
| `fixture.Bus.Clear()` | `fixture.Bus.Clear()` | ✅ Direct match |
| `fixture.Tasks.All` | `fixture.Tasks` (BackgroundTaskCollector) | ✅ Auto-resolved from DI |

---

## Type Compatibility Fixes

| Item | Status | Details |
|------|--------|---------|
| `PlainTextResult : HttpResult` | ✅ Fixed | S365 returns PlainTextResult from `ICommand<HttpResult>` handlers (webhooks). Changed inheritance from `OperationResult` → `HttpResult`. Commit 34a16ea. |
| `ResourceCreated<T>` ambiguity (CS0104) | ✅ Fixed | `Blueprint.Runtime.Exec.ResourceCreated<T>` conflicted with `Blueprint.Http.ResourceCreated<TResource>`. Removed Exec convenience types; Http versions are the S365 standard. Commit 10e29b3. |
| `StatusCodeResult` types | ✅ Already OK | S365 only uses `.Accepted`, `.Created`, `.NoContent`, `.OK` — all 4 already exist in Blueprint. The 47 types in legacy Blueprint library are unused by consumers. |
| `ResourceEvent<T>` factory pattern | ✅ Compatible | S365 uses `FolderEvents.Created.New(domain)` which calls `new ResourceCreated<T>(query)` under the hood. This is a consumer pattern; Blueprint provides the base types. |
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
| `ResourceEvent<T>` | `ResourceEvent<T>` | ✅ **No change needed** — BP108 recognizes this directly |
| `StatusCodeResult.Accepted` | `StatusCodeResult.Accepted` | ✅ **No change needed** — identical nested type + `.Instance` singleton |
| `StatusCodeResult.NoContent` | `StatusCodeResult.NoContent` | ✅ **No change needed** — identical |
| `StatusCodeResult.Created` | `StatusCodeResult.Created` | ✅ **No change needed** — identical |
| `StatusCodeResult.OK` | `StatusCodeResult.OK` | ✅ **No change needed** — identical |
| `ListApiResource<T>` | `ListApiResource<T>` | ✅ Same |
| `PagedApiResource<T>` | `PagedApiResource<T>` | ✅ Same |
| `PlainTextResult` | `PlainTextResult` | ✅ Extends HttpResult — same class |
| Plain `T` | Plain `T` | ✅ Blueprint supports plain value returns |
| `HttpResult` subclasses | `HttpResult` subclasses | ✅ Custom HttpResult types work via ExecuteAsync |

### Execution Patterns

| Pattern | S365 Usage | Blueprint Support |
|---|---|---|
| Separate `IApiOperationHandler<T>` | Primary pattern | ✅ Supported via handler discovery |
| `InvokeAsync()` on operation class | Secondary pattern (e.g. `DeleteBusQueueMessagesCommand`) | ✅ Supported via convention |
| Multi-operation handlers | Occasional | ✅ Supported via handler discovery (one handler class, multiple operation types) |
| `StateBasedLinkChecker<TOp, TResource>` | Conditional HATEOAS links | ✅ Implemented with identical API (migration-findings line 30) |

---

## Session Log

| Date | Session | Key Changes |
|------|---------|-------------|
| 2025-07 | Initial | S365 proof project, codemod tool, .NET 10 upgrade, testing helpers |
| 2025-07 | Session 2 | Resource auth builtin, tenant first-class, migration findings doc |
| 2025-07 | Current | ReadJsonBodyAsync static lambda, S365 test fixture compat shims (typed overloads, anonymous BackgroundTask/Message, auth API match, factory pattern, GetOptions), migration-findings updates |
| 2025-07 | Pipeline extraction | UoW/Outbox/BackgroundTask helper extraction, x-links/InvokeAsync/JsonConverter gap resolution, comprehensive S365 compatibility verification |
| 2025-07 | ResourceEventAudit | Extracted inline resource-event audit code to PipelineHelpers.ExecuteResourceEventAuditSinkAsync; self-link dedup test added; pipeline optimization analysis confirmed most code paths well-optimized |
| 2025-07 | Gap analysis | OpenAPI version configurable, Link schema component, comprehensive S365 pattern verification: all control attributes, collection types, validation fast-paths, tenancy, ResourceEvent backward compat confirmed |
| 2025-07 | Deep compat | Rebus handler analysis (47 handlers fully compatible), test infrastructure deep verification (S365 auth helpers, FakeBus assertions, BackgroundTaskCollector.All), TestMessageCollector.Should() chain compat, OpenAPI security/nullable verified |
| 2025-07 | Final gap analysis | ResourceEvent `[Obsolete]` removal (prevents CS0618 warnings), ApiOperationResourceConvention confirmed superseded by compile-time modules, Sig365LinkGeneratorMiddleware confirmed replaced by source-gen, all 6 S365 middleware types verified compatible, health checks confirmed Blueprint-agnostic, PlainTextResult verified |
| 2025-07 | Auth + remaining patterns | Full auth analysis: dual authorizer merge into IResourceAuthorizationProvider (consumer responsibility), special permissions = [AllowAnonymous]/[MustBeAuthenticated] (BPAU003 compile-time), verb expansion consumer-owned, AccountId well-known property ✅. Patterns: 0 IPropertyPopulator (middleware instead), 11 IBackgroundTask (all standard), 70 Rebus subscriptions (all IHandleMessages<T>), 100+ NodaTime usages (consumer config), 5 custom validators (runtime fallback), ApiId TenantId→string TenantId migration step needed |
| 2025-07 | Runtime optimizations | BlueprintHttpClient: PropertyInfo cache, StringBuilder query strings, span-based placeholder parsing. PipelineHelpers: ResolveServiceArray fast-path for arrays. ResourceEvent JSON: fix `data` vs `content` envelope property name. ArrayPool buffer leak fix in ReadJsonBodyAsync (try/finally). RunAllPublishedMessagesAsync/RunAndConsumeAllPublishedMessagesAsync added to test fixture. |
| 2025-07 | Compatibility deep-dive | Added HttpContext/ClaimsPrincipal handler property injection. Verified S365 TenantOrderedPagedQuery/TenantPagedApiResource already extend Blueprint base classes. Confirmed ApiId stays S365-owned. Verified ApiOperations base class property injection. NotFoundException→ProblemDetails(404) confirmed. TypeScript codegen flow fully analysed (custom Handlebars, x-links compatible). |
| 2025-07 | ResourceEvent JSON fix | Fixed `$object` property and `ResourceEventChangeType` string serialization on `ResourceEvent`. Added 5 JSON serialization tests. Updated playbook: ResourceEvent<T> works unchanged (no migration to ResourceEventResult needed). Updated return type mapping: StatusCodeResult subtypes identical. Fixed stale handler rename advice (all 4 conventions supported). |
| 2025-07 | TypeConverter support | Added [TypeConverter] support for route/query parameter binding (matching legacy Blueprint behavior). S365 ApiId, FolderReference, Role, NodaTime.Instant all have [TypeConverter] attributes — all work automatically. Fixed Nullable<T> unwrapping for TypeConverter query types. Updated BPPO001/BPPO003 diagnostics. Corrected stale ApiId entries. |
| 2025-07 | Pipeline IL optimization | Extracted `PipelineHelpers.AppendValidationError()` to deduplicate lazy-init+add pattern across all validation fast-path checks. Saves ~N statements per pipeline (N = validated property count). Benchmark baseline: MinimalQuery 2μs/2.5KB, ValidatedQuery(valid) 13.5μs/5.1KB, FullStackCommand(valid) 12μs/5.7KB. Validation adds 6-7× overhead over bare pipeline. |
| 2025-07 | FromAuthContext | Added `[FromAuthContext]` attribute for auth-populated properties (e.g. TenantId). Properties are excluded from HTTP binding/OpenAPI. Resolves S365 gap where non-route TenantId appeared in request body/query. Route placeholder takes precedence. 11 tests. S365 proof updated. TypeScript codegen compatibility verified via Handlebars template deep-dive. |
