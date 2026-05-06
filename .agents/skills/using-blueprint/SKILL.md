---
name: using-blueprint
description: Use when building, migrating, or debugging a consumer application that uses Blueprint's source-generated operation, app-registration, and HTTP execution model, especially for Signature365-style adoption
---

# Using Blueprint

## Overview

Blueprint is compile-time-first. Consumer code describes operations and app roots so the generator can emit descriptors, registrations, pipelines, and executors. Runtime code should consume generated knowledge, not rediscover the API surface at startup.

## When to Use

- Adding a Blueprint operation, app root, or HTTP host
- Migrating a legacy Blueprint or Signature365-style app to source-generated Blueprint
- Diagnosing missing generated descriptors, registrations, routes, or executor behavior
- Understanding which compile-time contract a consumer repo must satisfy

Do not use this skill to change Blueprint internals in `blueprint-source-gen`; use `extending-blueprint` for that.

**REQUIRED BACKGROUND:** Use `using-dotnet` and `dotnet-advisor` when the consumer repo is .NET-based. This skill adds Blueprint-specific contract guidance on top of that baseline.

## Quick reference

| Concern | Consumer contract |
| --- | --- |
| Operation type | Implement `ICommand` or `IQuery`, which extend `IOperation` |
| Descriptor | Expect `static ApiOperationDescriptor Configure()` to be the contract entry point |
| Multiple outcomes | Use `Results<T1, T2>` when an operation contract needs to declare one of two host-agnostic semantic result shapes; both T1 and T2 must be `OperationResult` subtypes (e.g., `Results<OkResult<T>, NotFoundResult<T>>` or `Results<ResourceEventResult<T>, ConflictResult<T>>`); implicit conversion from T1/T2 makes handler returns ergonomic but derived types like `TemplateResourceEvents.Published` need an explicit cast to the base union member type |
| Resource-event discriminator | When a body-bearing `Blueprint.Runtime.Exec.ResourceEventResult<T>` should participate in later descriptor/OpenAPI discrimination, declare `[ResourceEvent("...")]` on the payload type `T` so the event identity is part of the compile-time contract |
| App root | Create a `partial` `BlueprintApp` with `[BlueprintAppScan]`; use `FromAssemblyOf = typeof(...)` when operations live in another assembly |
| Operation grouping | Use `[ApiOperations("Billing")]` on an HTTP-exposed operation when the OpenAPI tag/group should come from an explicit grouped-operation name instead of the namespace-derived fallback |
| Serializer context | If the app maps Blueprint through the HTTP host (`MapBlueprintApi<...>()`) and any scanned operation can produce a body-bearing HTTP response contract, declare `[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(AppJsonContext))]` on the `BlueprintApp` root so the build can prove the app-owned `JsonSerializerContext` covers those generated HTTP contracts; missing declaration now fails with `BP007`, incomplete declared coverage fails with `BP006`, and the generated app now persists both the declared serializer-context type and the declared `JsonSerializable(...)` body-type inventory (`BlueprintApp.AppOwnedSerializerContextType` / `AppOwnedSerializerContextBodyTypeNames`) as the compile-time/OpenAPI ownership seam rather than a runtime serializer switch |
| Generator wiring | Reference Blueprint's generator as an analyzer so compile-time generation runs; in this repo the sample uses `ProjectReference` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"` |
| HTTP host | Map the generated surface with `MapBlueprintApi<TApp, TExec>()` |
| Authorization | `[Resource("noun", "verb")]` for scope-pair auth; `[FromResource(typeof(X))]` for scope inheritance; `[AllowAnonymous]` for public access; `[MustBeAuthenticated]` for identity-only checks without scopes |
| Sensitive data | `[Sensitive]` on properties signals audit sinks to redact/mask that data |
| Audit opt-out | `[DoNotAudit]` on a class opts out of automatic audit recording |
| Trace opt-out | `[DoNotRecordPerformanceMetrics]` on a class suppresses Activity/trace span creation |
| Validation | Prefer declarative `ValidationAttribute` usage; Blueprint generates validation from those contracts, and the current retained Blueprint-specific set now includes `Blueprint.Validation.GreaterThanPropertyAttribute` |
| Debugging | Prefer build diagnostics, generated output, manifests, and sample patterns over runtime guesswork |

## Advanced analyzer extensions

- Companion analyzer packages should derive from Blueprint's default descriptor/pipeline generator composition instead of replacing the built-in module set wholesale.
- Package-owned exception translators belong in generator modules that contribute through `IExceptionTranslationContributor`; that keeps ordering/conflict diagnostics and descriptor failure metadata aligned with Blueprint's built-in translators.

## Consumer companion generator packages

A consumer package can ship companion generators that extend Blueprint's compile-time behavior for custom hosts:

1. **Companion descriptor generator** — derives from `OperationDescriptorSourceGenerator`, calls `CreateDefaultConfig(new CustomHostModule())` in the constructor. Adds host exposure and custom descriptor metadata for operations matching a consumer-defined marker interface.
2. **Companion pipeline generator** — derives from `OperationExecutorPipelineSourceGenerator`, calls `CreateDefaultConfig(new CustomPipelineModule())` in the constructor. Adds host-specific pipeline steps conditionally for marked operations.
3. Both generators can run in the same compilation without conflicts (descriptor files use `*_Descriptor.g.cs` hint names, pipeline files use `*ExecutorPipeline.g.cs`).

### Custom host registration pattern

- Implement `IBlueprintSourceGeneratorModule`, `IDescriptorContributor`, and `IEnabledHostContributor` on a single module
- Set `Category = BlueprintModuleCategory.Core` and `Order = 0` (host modules MUST be Core to run before projection modules)
- Return the custom host name from `IEnabledHostContributor.HostNames` (e.g., `["messaging"]`)
- In `ContributeToDescriptor`, check `context.GetOperationTypeSymbol().AllInterfaces` for the consumer marker interface, then call `context.AddMetadata(new HostExposureRegistration("messaging", locationInfo))`

### Conditional pipeline module pattern

- Implement `IBlueprintSourceGeneratorModule` and `IPipelineContributor`
- In `ContributeToPipeline`, check `context.Operation.TypeSymbol.AllInterfaces` for the consumer marker
- Early-return if not matched — this ensures non-matching operations get the standard pipeline
- Use `BlueprintModuleCategory.Consumer` for pipeline-only modules (ordering relative to Core modules is fine here since pipeline modules don't need to run before projection)

At runtime, `BlueprintApp.GetOperationsForHost("custom-host")` returns operations whose generated descriptors carry `OperationHostExposureFeatureData(["custom-host"])` — the runtime mechanism is completely host-name-agnostic.

## Validation contract

- Treat validation attributes as compile-time inputs first and runtime behavior second; if the build can prove the contract is wrong, expect a diagnostic instead of a runtime repair path.
- `Blueprint.Validation.GreaterThanPropertyAttribute` is now part of the retained Blueprint-specific validation surface for property-to-property greater-than checks.
- The retained Blueprint-specific conditional validator set now includes `GreaterThan`, `RequiredIf`, `RequiredIfNot`, and `NotAllowedIf` alongside `GreaterThanProperty`; verify the exact fast-path versus fallback shape before relying on a specific constructor or comparison form.
- If an HTTP-exposed operation needs a consumer-owned OpenAPI tag/group that differs from the namespace-derived fallback, declare `[ApiOperations("...")]` on the operation type; the descriptor and OpenAPI seams now prefer that explicit group name when it is present.

## Authorization contract

- **Scope-pair model:** `[Resource("noun", "verb")]` declares that an operation requires a specific scope pair. Multiple `[Resource]` attributes on one operation use any-of semantics (the caller needs at least one).
- **Scope inheritance:** `[FromResource(typeof(X))]` inherits scope pairs from another operation's direct `[Resource]` declarations (not transitive). Cannot be combined with `[Resource]` on the same operation (BPAU005).
- **Implicit authentication:** Operations with `[Resource]` or `[FromResource]` automatically require an authenticated identity; you do NOT also need `[MustBeAuthenticated]`.
- **Identity-only checks:** Use `[MustBeAuthenticated]` when the operation needs an authenticated caller but has no specific scope requirements.
- **Anonymous access:** Use `[AllowAnonymous]` for public endpoints. Cannot be combined with `[Resource]` or `[MustBeAuthenticated]` (BPAU002/BPAU003).
- **Compile-time diagnostics:** BPAU001 (auth on non-HTTP op), BPAU002 (conflicting AllowAnonymous + MustBeAuthenticated), BPAU003 (Resource + AllowAnonymous), BPAU004 (empty noun/verb), BPAU005 (FromResource + Resource on same op), BPAU006 (FromResource target has no Resource attrs).

## Auditing and observability contract

- **Audit opt-out:** `[DoNotAudit]` (from `Blueprint.Auditing`) on an operation class suppresses the PostExecution audit sink call.
- **Sensitive properties:** `[Sensitive]` (from `Blueprint.Auditing`) on a public property signals audit sinks and logging pipelines to redact or mask that value. Does not affect HTTP/OpenAPI contracts.
- **Trace suppression:** `[DoNotRecordPerformanceMetrics]` (from `Blueprint.Auditing`) on an operation class suppresses Activity/trace span creation for noisy or polling endpoints.
- **Generated audit events:** The generated pipeline emits structured `blueprint.audit.started`, `blueprint.audit.succeeded`, and `blueprint.audit.failed` activity events with `blueprint.operation.type` tags when an `IAuditSink` is registered. Trace correlation is inherent via the enclosing Activity.

## Background tasks contract

Blueprint supports deferred task execution through `IBackgroundTaskScheduler`:

1. **Declare a task operation:** Implement `ICommand<T>` (or `ICommand<NoResultOperationResult>`) and also implement `IBackgroundTask` from `Blueprint.Tasks`. Mark with `[TaskHost]` to exclude from HTTP mapping and include in the task host.
2. **Enqueue from a handler:** Inject `IBackgroundTaskScheduler` as a handler DI parameter, call `scheduler.Enqueue(new MyTask { ... })`.
3. **Generated flush:** The `BackgroundTaskFlushModule` runs at Order=1000 in PostExecution — after UoW save and audit. It calls `scheduler.FlushAsync()` only on handler success.
4. **Task host dispatch:** `BlueprintApp.GetOperationsForHost("tasks")` returns all task-marked operations. The executor finds and runs the matching pipeline when a scheduled task arrives.

### Implementation pattern

```csharp
// In startup
blueprint.AddBackgroundTaskScheduler<InProcessBackgroundTaskScheduler>();
// Or for production: blueprint.AddBackgroundTaskScheduler<HangfireTaskScheduler>();

// In a handler that triggers async work
public static async Task<ResourceEventResult<Template>> HandleAsync(
    PublishTemplateCommand command,
    IBackgroundTaskScheduler scheduler)
{
    var result = /* do work */;
    scheduler.Enqueue(new SendTemplateSharedNotificationTask { TemplateId = command.Id });
    return result;
}
```

The flush-on-success semantic guarantees tasks are only dispatched when the command succeeds and after the unit-of-work is committed.

## Persistence contract (Unit of Work)

Blueprint's `UnitOfWorkModule` generates a `SaveChangesAsync` call for ICommand operations at Order=900 in PostExecution:

- **Only commands:** Queries never trigger save. `[DoNotSaveChanges]` on a non-command triggers diagnostic `BPP001`.
- **Only on success:** Save only runs when `handlerResult.WasSuccessful` is true.
- **Optional DI:** If no `IUnitOfWork` is registered, the step is skipped gracefully.
- **DI scope owns disposal:** The unit-of-work instance is disposed by the DI container scope, not by Blueprint. Register as scoped.
- **Opt-out:** `[DoNotSaveChanges]` on a command suppresses the generated save call.

### Implementation pattern

```csharp
// In startup — EF Core example
blueprint.AddUnitOfWork<EfCoreUnitOfWork>();

// IUnitOfWork implementation
public sealed class EfCoreUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
```

Pipeline order: Handler → UoW Save (900) → Audit (950) → Background Task Flush (1000). This ensures committed state is audited and tasks reference committed data.

## Result contract

- The first public semantic-union surface is `Results<T1, T2>` in `Blueprint.Model`.
- Plain non-null handler returns currently normalize to typed `Blueprint.Runtime.Exec.OkResult<T>`, and `void`/`Task` handlers normalize to the current no-result runtime shape.
- Explicit `Blueprint.OperationResult` returns pass through unchanged; matching `OkResult<T>` handlers remain compatible with plain declared contracts like `IQuery<T>` when the payload type matches, and `Results<T1, T2>` handlers must resolve to one of the declared member result types.
- Declared semantic result contracts now also surface descriptor feature data through `OperationResultOutcomesFeatureData`, which flattens `Results<T1, T2>` into ordered `OperationResultOutcome` entries for runtime consumers that inspect `Configure()`.
- Invalid `Results<T1, T2>` contracts now also fail descriptor generation with `BP111` before `OperationResultOutcomesFeatureData` can drift away from the declared semantic member set; plain CLR payload members and nested unions are rejected rather than projected as descriptor outcomes.
- `MapBlueprintApi(...)` now starts the runtime migration away from self-executing results by translating `OkResult<T>`, `CreatedResult<T>`, `AcceptedResult<T>`, `NotFoundResult<T>`, `ConflictResult<T>`, host-neutral `NoResultOperationResult`, and the first host-owned no-content result `Blueprint.Runtime.Http.HttpNoContentResult` through `Blueprint.Runtime.Http.HttpOperationResultExecutor`; unsupported legacy results still fall back to direct `OperationResult.ExecuteAsync(...)` for now.
- Treat `NoResultOperationResult` as the default semantic success shape that currently maps to empty HTTP `200 OK`, and use `Blueprint.Runtime.Http.HttpNoContentResult` when the consumer contract intentionally needs an explicit HTTP `204 No Content` response instead of folding that transport choice back into the core result taxonomy.
- Use `Blueprint.Runtime.Exec.CreatedResult<T>` when the consumer contract should communicate "resource created" semantics with a response body; the current HTTP host maps it to JSON `201 Created`, so it stays distinct from both `OkResult<T>` (`200`) and the empty-body `KeyOnlyResourceEventResult` path.
- Use `Blueprint.Runtime.Exec.ResourceEventResult<T>` when the consumer contract needs a body-bearing resource-event shape with stable `eventId` and `resourceId` identity plus a payload; the current HTTP host maps it to a JSON `201 Created` envelope with top-level `eventId`, `resourceId`, and `content`.
- When a `ResourceEventResult<T>` payload should participate in same-status descriptor/OpenAPI discrimination, declare `[ResourceEvent("...")]` on the payload type `T`; the generator now projects that compile-time event identity through `OperationResourceEventMetadataFeatureData` instead of guessing from runtime constructor calls.
- When every member of a same-status HTTP union is a body-bearing `ResourceEventResult<T>` whose payload exposes `[ResourceEvent("...")]`, the generated OpenAPI document now emits an `eventId`-discriminated `oneOf` envelope for that `201` response family rather than rejecting the union as ambiguous.
- Use `Blueprint.Runtime.Exec.AcceptedResult<T>` when the consumer contract should communicate "accepted but not yet completed" semantics with a response body; the current HTTP host maps it to JSON `202 Accepted`, so it stays distinct from both immediate `OkResult<T>` success (`200`) and created-resource responses (`201`).
- Use `Blueprint.Runtime.Exec.NotFoundResult<T>` when the consumer contract should communicate a missing-resource outcome with a response body; the current HTTP host maps it to JSON `404 Not Found`, so it stays distinct from success-shaped responses and from state/conflict results. See `GetTemplateQuery` in the reference app for a working `Results<OkResult<T>, NotFoundResult<T>>` example that maps to 200/404 via the generated pipeline.
- Use `Blueprint.Runtime.Exec.ConflictResult<T>` when the consumer contract should communicate a domain or state conflict with a response body; the current HTTP host maps it to JSON `409 Conflict`, so it stays distinct from success-shaped responses and can participate in declared `Results<T1, T2>` contracts without collapsing into the `200`/`201`/`202` paths. See `PublishTemplateCommand` in the reference app for a working `Results<ResourceEventResult<T>, ConflictResult<T>>` example where re-publishing returns 409.
- Treat `Blueprint.Runtime.Exec.ValidationFailedResult` as the canonical semantic validation-failure name when generated validation or consumer contracts need to talk about known input-contract failures; the older `Blueprint.Middleware.ValidationFailedOperationResult` name now exists only as a compatibility alias.
- `ValidationFailedResult` is now on the supported HTTP side of the current host boundary too: host-exposed operations may declare it, and the current HTTP host translates descriptor-advertised validation-failure families to `422 Unprocessable Entity` problem responses.
- Do not declare `Blueprint.Runtime.Exec.UnhandledExceptionResult` or `Blueprint.UnhandledExceptionOperationResult` in consumer result contracts; those names identify the generated pipeline's internal fallback shell, and `BP111` keeps them out of public semantic outcome contracts until later error-model work defines explicit translation for that boundary.
- Use `Blueprint.Runtime.Exec.KeyOnlyResourceEventResult` for the first host-neutral key-only resource-event semantic shape when the result carries event/resource identity but intentionally returns no HTTP body; the current HTTP host translates it to empty `201 Created` responses by convention.
- Keep `ResourceEventResult<T>` and `KeyOnlyResourceEventResult` distinct: the former is the future-friendly body-bearing event path for same-status discrimination work, while the latter stays the empty-body event path and does not qualify for that discriminator carve-out.
- Host-exposed operations now also fail build-time validation with `BP108` when the declared semantic result contract includes a shape that the current enabled HTTP executor path does not translate.
- Internal unhandled-fallback results remain on that unsupported side of the current host boundary, even though the HTTP host can translate descriptor-advertised fallback failures at runtime.
- Host-exposed `Results<T1, T2>` contracts now also fail build-time validation with `BP109` when multiple supported semantic result members would still collapse to the same current HTTP response shape, such as multiple `OkResult<T>` members that all translate to `200` JSON responses.
- Host-exposed `Results<T1, T2>` contracts now also fail build-time validation with `BP110` when supported members still share the same HTTP status code without explicit host/client discrimination metadata, such as `OkResult<T>` plus `NoResultOperationResult` both mapping to `200 OK`.
- Host-exposed `Results<T1, T2>` contracts now also fail build-time validation with `BP112` when they try to mix host-neutral `NoResultOperationResult` with host-owned no-content/status-only result families such as `Blueprint.Runtime.Http.HttpNoContentResult`; that semantic split is deliberate, so use one shape or the other instead of combining them.
- Nullable plain-value handler returns are a build-time error, and broader host translation remains follow-on work.

## Service registration (DX)

Blueprint provides `ServiceCollectionExtensions` for discoverable fluent registration:

```csharp
services.AddBlueprintApi<MyApp, MyAppExecutor>(blueprint =>
{
    blueprint.AddAuditSink<MyAuditSink>();
    blueprint.AddResourceEventAuditSink<MyResourceEventAuditSink>();
    blueprint.AddResourceAuthorizationProvider<MyAuthProvider>();
    blueprint.AddBackgroundTaskScheduler<MyTaskScheduler>();
    blueprint.AddUnitOfWork<MyUnitOfWork>();
    blueprint.AddHttpResultExecutor<MyCustomExecutor>();
    blueprint.ConfigureHttp(options => options.PublicHost = "https://api.example.com");
});
```

- `AddBlueprintApi<TApp, TExec>()` registers the app and executor singletons
- `BlueprintServiceBuilder` exposes typed methods for all Blueprint infrastructure contracts
- Scoped services (UoW, task scheduler) are registered scoped; singletons (audit, auth) as singletons
- `ConfigureHttp` binds `BlueprintHttpOptions` for link generation base URI and error detail exposure

## ASP.NET Core infrastructure (consumer responsibility)

Blueprint intentionally delegates standard ASP.NET Core concerns to the consumer rather than wrapping them:

| Concern | Consumer action | Blueprint interaction |
| --- | --- | --- |
| **CORS** | `services.AddCors()` + `app.UseCors()` | None — Blueprint endpoints are just route endpoints |
| **Health checks** | `app.MapHealthChecks("/health")` | None — add alongside `MapBlueprintApi` |
| **Rate limiting** | `services.AddRateLimiter()` + `app.UseRateLimiter()` | None — standard middleware order applies |
| **Request size limits** | Kestrel `MaxRequestBodySize` or `[RequestSizeLimit]` | None |
| **OpenTelemetry** | `services.AddOpenTelemetry().WithTracing(b => b.AddSource("Blueprint"))` | Blueprint emits all spans on `BlueprintActivitySource.ActivitySourceName` |
| **JSON naming policy** | `[JsonSourceGenerationOptions(PropertyNamingPolicy = ...)]` on your `JsonSerializerContext` | The app-owned serializer context controls all serialization behavior |

### OpenTelemetry integration example

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(Blueprint.Diagnostics.BlueprintActivitySource.ActivitySourceName)
        .AddAspNetCoreInstrumentation());
```

Blueprint emits:
- A span per operation execution (named by operation type)
- `blueprint.audit.started/succeeded/failed` events with `blueprint.operation.type` tags
- Exception events following OTel `exception.*` semantic conventions
- `blueprint.operation.validated`, `blueprint.operation.authorized` status events

## Working loop

1. Start from the consumer contract: operation -> app root -> host wiring.
2. Check build diagnostics before editing runtime code.
3. Compare against the sample consumer story when available (`Blueprint.SourceGenerator.Sample` and `.Sample.Contract` in the Blueprint repo).
4. For migrations, work in dependency order: bootstrap and discovery, pipeline and result handling, auth and validation, resource and link behavior, OpenAPI and inspection, then consumer-specific integrations.

## Common mistakes

- Treating Blueprint as runtime-discovery-first
- Hand-registering operations instead of relying on generated registration
- Forgetting `partial`, `[BlueprintAppScan]`, or analyzer wiring
- Forgetting to declare `[BlueprintAppSerializerContext(...)]` for an app that maps Blueprint through `MapBlueprintApi<...>()` and has body-bearing HTTP response contracts; the current build-time repair path is to add the declaration so `BP007` can stop failing before `BP006` starts checking completeness
- Forgetting to keep `[BlueprintAppSerializerContext(...)]` aligned with the app-owned `JsonSerializerContext`; the current build-time repair path is to add the missing `JsonSerializable(typeof(...))` coverage required by `BP006`
- Expecting runtime fallbacks to repair a missing compile-time contract
- Ignoring generator diagnostics or generated output diffs

## Error handling patterns

### Discriminated union results (preferred)

Handlers should declare error outcomes in their return type for compile-time safety and OpenAPI projection:

```csharp
public Results<OkResult<Item>, NotFoundResult<Item>> Handle(GetItemQuery query)
{
    var item = db.Find(query.Id);
    return item is null
        ? new NotFoundResult<Item>(new Item { Id = query.Id })
        : new OkResult<Item>(item);
}
```

Available result types: `OkResult<T>`, `CreatedResult<T>`, `AcceptedResult<T>`, `NotFoundResult<T>`, `ConflictResult<T>`, `HttpNoContentResult`, `NoResultOperationResult`.

### Typed exceptions (escape hatch)

For cases where structured results are impractical (deeply nested code, cross-cutting concerns), throw typed exceptions:

```csharp
throw new NotFoundException("Template 123 not found");   // → HTTP 404
throw new ConflictException("Version conflict");          // → HTTP 409
throw new BadRequestException("Invalid date range");      // → HTTP 400
throw new ForbiddenException("Insufficient permissions"); // → HTTP 403
throw new ApiException("Custom error", 422);              // → HTTP 422
```

- Exceptions with `HttpStatus < 500` are logged at `Information` level (non-fatal)
- Exceptions with `HttpStatus >= 500` are logged at `Error` level with OTel ERROR status
- In development (`ExposeExceptionDetailsInErrorResponses = true`), all exceptions expose message and stack trace
- In production, 5xx errors return a generic message; 4xx errors expose the exception message

### ExposeExceptionDetailsInErrorResponses

Configure in startup for environment-appropriate behavior:

```csharp
blueprint.ConfigureHttp(http =>
{
    http.ExposeExceptionDetailsInErrorResponses = env.IsDevelopment();
});
```

This is the canonical consumer-installable Blueprint skill. If Blueprint changes the consumer contract, update this skill in the same change as the code and docs.
