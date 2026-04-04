# Result model and host translation

- Status: Planned
- Priority: P1
- Classification: Core/result
- Depends on: `../foundation-module-system/plan.md`; co-designed with `../pipeline-composition-and-execution/plan.md` so result taxonomy and normalization rules are agreed before either implementation hard-codes them

## Why this module exists

The current rewrite still treats result types as if they belong to the runtime/host layer. That is too limiting for the migration now that:

- operation contracts need to declare typed one-of-many outcomes
- resource events need to be concrete contract-visible result types
- HTTP-specific behavior such as `ETag` and cache control must be expressible without baking HTTP execution into every result type

The rewrite needs a single canonical result model that is:

- visible from contract-facing model/abstractions
- host agnostic
- analyzable at compile time
- compatible with typed unions and future non-HTTP hosts

## Evidence

- Current: `src\Blueprint.Runtime\Exec\OperationResult.cs`
- Current: `src\Blueprint.Runtime\Exec\OkResult.cs`
- Current: `src\Blueprint.Runtime\Exec\ValidationFailedResult.cs`
- Current: `src\Blueprint.Runtime\Exec\ValidationFailedOperationResult.cs`
- Current: `src\Blueprint.Runtime\Exec\UnhandledExceptionOperationResult.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- Legacy runtime/result evidence: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\`
- Legacy host-neutral no-op result: `D:\Projects\Signature365\src\Blueprint\Blueprint\NoResultOperationResult.cs`
- Legacy HTTP-specific result families: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\StatusCodeResult.cs`, `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\HttpResult.cs`, `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\PlainTextResult.cs`
- Legacy testing helpers: `D:\Projects\Signature365\src\Blueprint\Blueprint.Testing\ResultTestingExtensions.cs`
- Dependent module plan: `../resource-model-links-and-post-processing/plan.md`

## Migration goal

Redesign Blueprint results so they express **semantic outcomes**, while hosts such as HTTP translate those outcomes into status codes, headers, bodies, and other transport behavior.

That means:

- typed success results remain available
- failure results remain explicit
- typed unions (`Results<T1, ...>`) become part of the public contract surface
- resource-event results become concrete contract-level result types
- HTTP semantics travel as attachable hints rather than as hard-coded execution behavior on the result classes themselves

## Breaking change path from self-executing results

The current runtime result shape is an active architectural contradiction:

- `OperationResult` owns `ExecuteAsync(...)`
- concrete runtime results still look host-executing rather than host-translated

This plan should therefore treat the change as an explicit breaking-change workstream, not as a quiet refactor.

Planning direction:

- semantic result types become data-first and host agnostic
- hosts gain explicit translation/execution responsibility
- direct `OperationResult.ExecuteAsync(...)` use becomes a migration target rather than the long-term contract
- legacy runtime result types such as `ValidationFailedOperationResult` become migration aliases or shims for the canonical semantic names where that reduces migration pain

This breaking change needs to stay aligned with `../breaking-changes-and-migration-tooling/plan.md` so analyzers, code fixes, and compatibility shims can be planned deliberately instead of after implementation starts.

## Canonical result taxonomy

The result model should stop being an unstructured grab-bag of ad hoc runtime types and become a small, named taxonomy.

### Shared semantic results

The canonical shared families should include:

- `OkResult<T>` or a very close equivalent for success with a typed body
- an untyped `OkResult` only as a compatibility escape hatch, not the preferred contract shape
- `CreatedResult<T>` as a first-class semantic result, not just an HTTP hint on `OkResult<T>`
- `AcceptedResult` / `AcceptedResult<T>` as the semantic "accepted but not completed" shape, even if it lands after the first cut
- a host-neutral `NoResult` / `NoResultOperationResult` equivalent for success with nothing meaningful to return
- `ValidationFailedResult` for known input-contract failures
- `NotFoundResult` for semantic missing-resource outcomes
- `ConflictResult` for semantic state/domain conflicts
- an internal unhandled-exception result shape that is not valid as a public contract outcome

The runtime audit shows explicit precedent for both:

- a host-neutral no-op result (`NoResultOperationResult`)
- HTTP-specific status/result families (`StatusCodeResult`, `PlainTextResult`, `HttpResult`)

The new model should preserve that distinction rather than collapsing everything into one generic "no body" shape.

### Resource-event result families

Resource-event results build on the shared taxonomy but remain distinct semantic shapes:

- body-bearing resource-event results
- key-only / identity-only resource-event results

Key-only resource events must stay semantically distinct from `NoResult`. They may produce no HTTP body, but they still carry:

- event identity
- resource identity
- audit/persistence semantics

### Typed unions

`Results<T1, ...>` is the union layer over concrete semantic result types.

It is the canonical way to describe one-of-many allowed outcomes for an operation contract.

## Runtime support to build

- A shared host-agnostic result abstraction in the contract-visible model/abstractions layer
- Typed success-result shapes such as `OkResult<T>` or a close equivalent
- Typed one-of-many unions (`Results<T1, ...>`) for operation contracts and handler returns
- Semantic failure-result shapes used by exception translation and validation
- A transport-neutral hint model that any result can carry
- Host executors/adapters that translate semantic results plus hints into transport behavior
- Generator/runtime support for normalizing plain handler return values into semantic result shapes
- A host-neutral no-result shape that is explicitly distinct from key-only resource-event results
- Host-specific result families/adapters for cases like explicit status-only or plain-text HTTP responses
- Test-support helpers that make semantic result assertions readable and diagnostic-friendly
- A flattened outcome model that downstream descriptors/OpenAPI/client generators can consume without reasoning about `Results<T1, ...>` directly

## Consumer configuration and activation

Consumers should not need a special activation switch to participate in the new result model.

Instead, they should be able to:

- return plain values where a simple success result is fine
- return typed semantic result types where they need explicit outcome control
- declare one-of-many result outcomes in operation contracts with `Results<T1, ...>`
- attach host-specific hints only when they need host-specific behavior

## Canonical direction

### 1. Result types become host agnostic

Result types should stop executing themselves.

They should describe:

- success/failure semantics
- any returned value/body
- any host-neutral metadata
- any attachable host hints

The active host is responsible for execution/translation.

### 2. Typed success results remain first-class

The migration should keep a typed success-result surface such as `OkResult<T>` or a very close equivalent because it is useful for:

- testing
- typed unions
- explicit success semantics
- resource/event results that still return a body

The important change is not the name; it is that the type becomes data-first and host agnostic.

### 3. Handler returns normalize into semantic results

The generator should normalize handler return types statically, not by adding runtime type checks in the hot path.

Canonical normalization rules:

- `void` / `Task` -> host-neutral `NoResult`
- plain `T` (non-result) -> `OkResult<T>`
- explicit semantic result type -> pass through unchanged
- `Results<T1, ...>` -> extract the inner concrete semantic result

Important edge rule:

- plain-value handlers returning nullable reference/value types should be a diagnostic error rather than silently normalizing `null`

### 3a. First implementation slice

The first result-model implementation pass should stay narrow enough to unblock the rest of Phase 1 safely.

Minimum scope:

- the initial `Results<T1, ...>` public shape
- static handler-return normalization into semantic results
- the first flattened-outcome descriptor model
- a migration path away from self-executing runtime results
- the first diagnostics for unsupported union members and ambiguous nullable plain-value returns

Later result families and host-specific conveniences should build on that slice rather than blocking it.

### 4. Typed unions are part of the public result contract

Blueprint should add a host-agnostic `Results<T1, ...>` union model inspired by Minimal APIs.

That union becomes the canonical way to declare one-of-many allowed outcomes in operation contracts.

Constraints and ergonomics:

- type arguments must be concrete supported semantic result types
- nested `Results<Results<...>, ...>` is not allowed
- duplicate member types are not allowed
- internal-only failure/result types are not valid public union members
- union helpers should provide implicit conversions plus readable `IsT*` / `AsT*` accessors

Descriptor/OpenAPI rule:

- `Results<T1, ...>` is the operation-contract wrapper
- generated descriptors should expose the **flattened outcomes**, not the raw wrapper type
- the raw wrapper remains a generator/runtime concern for handler normalization and generated client return types
- same-status outcome groups remain invalid by default unless the generator can prove a stable discriminator strategy from compile-time metadata

### 5. Host hints are attachable metadata, not inheritance branches

HTTP-specific behavior such as:

- `ETag`
- cache control
- explicit headers
- content-negotiation hints

should be expressed as attachable hints on any result.

The preferred shape is:

- a host-neutral hint collection on the semantic result
- typed sealed hint objects provided by the relevant host package
- fluent `With*` extension methods from the host package rather than raw mutable collection access

Illustrative direction:

```csharp
return new OkResult<TemplateApiResource>(resource)
    .WithHttpETag(resource.ETag)
    .WithHttpCacheControlPublic(TimeSpan.FromMinutes(5));
```

The result stays host agnostic; the HTTP host decides how to interpret the hints.

Important guardrails:

- unknown hints for an active host should never disappear silently
- duplicate hints of the same semantic kind need an explicit rule; the current recommendation is **last hint of a given type wins**, documented and enforced consistently
- hints are ephemeral execution metadata and should not become part of normal payload serialization

### 6. Resource-event results build on this model rather than reinventing it

Resource-event result types belong to the resource-model module, but they must sit on top of this shared result architecture:

- typed one-of-many outcomes
- semantic result shapes
- host-neutral execution
- attachable HTTP hints

The resource-model plan should not own a second independent result system.

Resource events are the main supported exception to the default "distinct status codes" rule for HTTP-visible unions:

- if every outcome in a same-status group is a body-bearing resource-event result
- and the generated HTTP body always carries a compile-time-stable `eventId` discriminator

then the generator can auto-emit same-status union metadata safely for descriptors/OpenAPI/generated clients.

### 7. Host-specific result families stay out of the core taxonomy

The runtime audit shows existing HTTP-native families such as:

- `StatusCodeResult`
- `PlainTextResult`
- `HttpResult`

Round 6 should not move those directly into the shared core taxonomy.

Instead:

- the core result model stays host agnostic
- hosts may keep host-specific compatibility result families in their own packages
- those host-specific families translate into or sit alongside the shared semantic model rather than defining it

### 8. No-body outcomes are first-class and distinct

The result model must distinguish between:

- `NoResult` = success, nothing meaningful to return, host-neutral
- host-specific explicit no-content/status-only results = transport-shaped compatibility or host convenience
- key-only resource-event results = success, no body by default, but still carrying resource/event identity

Those two shapes must not collapse into each other just because both often map to an empty HTTP body.

### 9. Testing and generated-client ergonomics are part of the design, not an afterthought

The result model should ship with a deliberate testing surface:

- result assertions that describe the actual semantic type seen
- good diagnostics when the wrong result shape is returned
- explicit assertion helpers for host hints where needed

For generated clients and HTTP-visible unions, the model also needs an ambiguity rule:

- by default, `Results<T1, ...>` members should map to distinct HTTP status codes for HTTP-visible operations
- body-bearing resource-event outcomes are the main supported exception: if every outcome in the same-status group carries a generated serialized `eventId`, the generator may auto-emit the discriminator strategy from compile-time metadata
- key-only/no-body resource-event outcomes do not qualify for that auto-discrimination path
- any other same-status union still needs an explicit opt-in with a declared body discriminator strategy rather than an accidental default

## Compile-time checks to add

- `Results<T1, ...>` may only include supported semantic result types
- `Results<T1, ...>` may not nest or repeat member types
- handlers must return a type compatible with the declared union or declared success shape
- plain-value handler returns may not be nullable unless an explicit semantic no-body/failure result is used
- conflicting or unsupported host hints for a declared host/result combination
- explicit result type declared but no host executor/adapter exists for an enabled host
- semantic result shapes that would be ambiguous to translate for a declared host
- HTTP-visible unions must have an unambiguous host/client discrimination strategy
- flattened descriptor outcomes must stay aligned with the declared `Results<T1, ...>` member set
- host-neutral no-result shapes and host-specific no-content/status-only shapes must not be conflated accidentally in descriptor generation
- same-status resource-event groups may only auto-flatten when every member in the group is body-bearing and exposes the generated serialized `eventId`
- same-status groups that mix body-bearing resource-event outcomes with key-only/no-body outcomes must be rejected at compile time

## Observability and debuggability

- generated per-operation result-contract manifest
- generated flattened outcome manifest per operation
- host translation summary showing which executor/adapter handles each result shape
- diagnostics that explain why a result type or host hint is unsupported
- explicit mapping from semantic result shapes to HTTP behavior for HTTP-enabled operations
- test-support helpers that report actual result type, attached hints, and relevant payload data clearly

## Explicit decisions

- Keep the shared result abstraction host agnostic.
- Keep typed success results such as `OkResult<T>` or a close equivalent.
- Keep a host-neutral `NoResult` / `NoResultOperationResult` equivalent as a first-class part of the taxonomy.
- Keep semantic created/accepted result shapes available instead of encoding them only as HTTP hints.
- Make `Results<T1, ...>` part of the public contract model.
- Normalize plain handler values into semantic results statically during generation rather than with runtime type checks.
- Treat `Results<T1, ...>` as an operation-contract wrapper, but treat flattened outcomes as the canonical descriptor/OpenAPI shape.
- Express HTTP-specific behavior through attachable hints rather than by embedding HTTP execution into result types.
- Use typed sealed hint objects plus fluent host-package extension methods as the preferred hint shape.
- Let hosts execute/translate results; result types themselves should not own transport execution.
- Let resource-event results build on this shared result model instead of defining a separate parallel outcome system.
- Keep host-specific result families such as explicit status-only or plain-text HTTP results in host packages rather than promoting them into the core taxonomy.
- Lock host-neutral `NoResultOperationResult` to the default empty HTTP `200 OK` translation path, and keep explicit no-content/status-only HTTP responses in host-owned result families instead of folding them into that core semantic shape.
- Default the first host-neutral key-only resource-event semantic shape to empty HTTP `201 Created` by convention, and defer explicit host-hint overrides until a later slice that deliberately introduces them.
- Land `CreatedResult<T>`, `AcceptedResult` / `AcceptedResult<T>`, and `ConflictResult` in the first implementation pass, but execute those families as separate narrow slices starting with `CreatedResult<T>` so the body-bearing `201 Created` pattern is proven before the accepted/conflict variants widen the semantic surface.
- Require distinct HTTP status codes by default for HTTP-visible unions; same-status unions require explicit discriminator opt-in.
- Allow body-bearing resource-event unions to auto-satisfy the same-status rule through a generated serialized `eventId` discriminator; key-only/no-body event outcomes are not part of that carve-out.
- Treat legacy `ValidationFailedOperationResult`-style names as compatibility aliases where needed, but converge the semantic model on canonical data-first result names.

## Open questions to resolve

- How should `IQuery<T>` / `ICommand<T>` / `IReturn<T>` metadata interact with `Results<T1, ...>` so descriptor generation and OpenAPI/client generation see the expanded contract rather than a raw wrapper type?

## Sequencing

Close the result taxonomy and normalization rules together with the pipeline plan before HTTP, error handling, OpenAPI, and the full resource-event model depend on a stable result contract.

## Durable progress

- Live active/next/last/gate tracking for this workstream lives only in `..\progress.json`. This durable-progress section records approved slice sequencing and rationale; it is not the live tracker.
- First approved paired-loop slice: introduce the first public host-agnostic `Results<T1, T2>` contract surface in `Blueprint.Model` with focused contract-layer tests. This gives the result-model and pipeline design loop a concrete union shape without yet changing pipeline return normalization, flattened descriptor outcomes, or compatibility shims.
- Completed the first result-model foundation slice:
  - `Blueprint.Model.Results<T1, T2>` now provides implicit conversions plus `IsT1`/`AsT1` and `IsT2`/`AsT2`.
  - The new contract-layer tests pin both wrong-member access and default-state failures.
  - The consumer guidance in `..\..\.agents\skills\using-blueprint\SKILL.md` now points at the new two-outcome contract surface.
- Completed the handler-return normalization slice:
  - `HandlerExecutionModule` now normalizes plain non-null handler values into typed `Blueprint.Runtime.Exec.OkResult<T>`, `void`/`Task` into `Blueprint.Runtime.Exec.NoResultOperationResult`, explicit `Blueprint.OperationResult` returns through unchanged, and `Results<T1, T2>` handlers into direct concrete-member returns.
  - The slice follow-up now also preserves explicit `Blueprint.Runtime.Exec.OkResult<T>` handlers against matching plain declared contracts such as `IQuery<T>` / `ICommand<T>` so explicit success semantics remain compatible with the simpler declared contract surface when the payload type matches.
  - Pipeline diagnostics `BP105`-`BP107` now reject nullable plain-value returns, unsupported union members, and handler returns that do not match the declared operation result contract.
  - The proof matrix now includes reviewed pipeline snapshots, compile-time diagnostic tests, runtime execution coverage in `src\Blueprint.SourceGenerator.Tests.Integration\Generators\Pipelines\HandlerReturnNormalizationIntegrationTests.cs`, and a green repo verification matrix (`dotnet format`, the main generator tests, the CodeWriter tests, the integration tests, and `dotnet build .\Blueprint.sln`).
- Completed the flattened descriptor outcomes slice:
  - `FlattenedResultOutcomeModule` now projects declared semantic result contracts into additive `OperationResultOutcomesFeatureData`, flattening `Results<T1, T2>` into ordered `OperationResultOutcome` entries and surfacing single declared semantic result contracts as one-outcome descriptor metadata.
  - Descriptor generation now reuses shared result-contract helpers, and contract-only compilations that do not reference `Blueprint.OperationResult` skip flattened outcome projection instead of failing generation.
  - The proof matrix now includes a red-to-green descriptor source proof for flattened outcomes, runtime `Configure().GetFeatureData<OperationResultOutcomesFeatureData>()` coverage for union and single semantic-result contracts, a contract-only compilation regression test, and a green repo verification matrix (`dotnet format`, the main generator tests, the CodeWriter tests, the integration tests, and `dotnet build .\Blueprint.sln`).
- Completed the first runtime-result migration slice:
  - `MapBlueprintApi(...)` now routes supported success shapes through `Blueprint.Runtime.Http.HttpOperationResultExecutor`, translating `OkResult<T>` payloads to JSON `200 OK` responses and `NoResultOperationResult` to empty `200 OK` responses without relying on `OkResult.ExecuteAsync(...)`.
  - Unsupported legacy results still fall back to direct `OperationResult.ExecuteAsync(...)`, making the new host-owned executor seam an explicit migration path rather than a quiet all-at-once replacement of the older contract.
  - The proof matrix now includes host-surface endpoint tests covering translated `OkResult<T>`, translated `NoResultOperationResult`, and the legacy fallback boundary, plus a green `Blueprint.SourceGenerator.Tests` project run.
- Completed the first host-executor availability diagnostic slice:
  - `HostExecutorAvailabilityModule` now runs during descriptor generation, reuses the flattened semantic-result helper surface, and reports `BP108` when an HTTP-exposed operation declares a semantic result shape that the current enabled HTTP executor path does not translate.
  - Supported `OkResult<T>` and `NoResultOperationResult` contracts stay clean, while mixed `Results<T1, T2>` unions report only the unsupported semantic-result members.
  - The proof matrix now includes a focused red-to-green descriptor diagnostic test file (`OperationDescriptorSourceGeneratorTests.HostExecutorAvailability.cs`) plus a green `Blueprint.SourceGenerator.Tests` project run.
- Completed the first ambiguous host-translation diagnostic slice:
  - `AmbiguousHostTranslationModule` now runs after the host-executor availability check and reports `BP109` when an HTTP-exposed `Results<T1, T2>` contract contains multiple supported semantic-result members that still collapse to the same current HTTP response shape.
  - The first approved ambiguity rule stays intentionally narrow: it targets current same-shape HTTP success unions such as multiple `OkResult<T>` members that all translate to `200` JSON responses, while unsupported members remain owned by `BP108`.
  - The proof matrix now includes a red-to-green descriptor diagnostic test file (`OperationDescriptorSourceGeneratorTests.AmbiguousHostTranslation.cs`) covering the ambiguous same-shape union, a clean single-result regression, the BP108/BP109 boundary, and a green `Blueprint.SourceGenerator.Tests` project run.
- Completed the first HTTP union discrimination diagnostic slice:
  - `HttpUnionDiscriminationStrategyModule` now runs after the host-translation ambiguity check and reports `BP110` when an HTTP-exposed `Results<T1, T2>` contract keeps the same current HTTP status code across supported members without an explicit host/client discrimination strategy.
  - The first approved discrimination rule stays intentionally narrow: it targets same-status mixed-shape unions such as `OkResult<T>` plus `NoResultOperationResult`, while unsupported members remain owned by `BP108` and same-shape collisions remain owned by `BP109`.
  - The proof matrix now includes a red-to-green descriptor diagnostic test file (`OperationDescriptorSourceGeneratorTests.HttpUnionDiscrimination.cs`) covering the same-status mixed-shape union plus the `BP108`/`BP109` boundaries, a green `Blueprint.SourceGenerator.Tests` project run, and a green `dotnet build .\Blueprint.sln --no-restore` verification pass.
- Completed the first flattened descriptor outcome-alignment diagnostic slice:
  - `FlattenedResultOutcomeModule` now reports `BP111` before descriptor emission when a `Results<T1, T2>` contract would force `OperationResultOutcomesFeatureData` away from the declared semantic member set, such as plain CLR payload members or nested union members.
  - The descriptor and pipeline seams now share the same union-member validity predicate, so unsupported `Results<T1, T2>` members no longer pass descriptor projection while only failing later in handler validation.
  - The proof matrix now includes a red-to-green descriptor diagnostic test in `OperationDescriptorSourceGeneratorTests.FlattenedOutcomes.cs`, a green `Blueprint.SourceGenerator.Tests` project run (`244` tests), a green targeted integration check for `AppDataModelSourceGeneratorResultOutcomesFeatureDataTests`, and a green `dotnet build .\Blueprint.sln --no-restore` verification pass.
- Repaired the queued no-result-shape separation follow-on:
  - The previously queued diagnostic depends on an unresolved design question in this plan (`What is the default HTTP status mapping for host-neutral NoResult, and how should that interact with explicit host-specific no-content/status-only result families?`) and the current codebase still exposes only host-neutral `NoResultOperationResult`, not a concrete host-specific no-content/status-only result family to write a failing proof against.
  - That makes the diagnostic slice non-executable today, so the next ready slice must first lock the default HTTP status mapping plus the ownership model for host-specific result families.
- Completed the default HTTP status mapping and host-specific result-family ownership slice:
  - Host-neutral `Blueprint.Runtime.Exec.NoResultOperationResult` is now explicitly locked to the default empty HTTP `200 OK` translation path when `MapBlueprintApi(...)` uses `Blueprint.Runtime.Http.HttpOperationResultExecutor`.
  - `Blueprint.Runtime.Http.HttpNoContentResult` is now the first concrete host-owned no-content result family member, translating to empty HTTP `204 No Content` without widening the core semantic result taxonomy.
  - `HostExecutorAvailabilityModule` now treats that HTTP-owned no-content result as part of the current HTTP executor surface, and the proof matrix includes a red-to-green descriptor support test plus a red-to-green endpoint translation test in `OperationResultTranslationEndpointTests`.
- Completed the first no-result-shape separation diagnostic slice:
  - `NoResultShapeSeparationModule` now runs between host-translation ambiguity and HTTP same-status discrimination, and reports `BP112` when an HTTP-visible `Results<T1, T2>` contract mixes host-neutral `NoResultOperationResult` with host-owned no-content/status-only result families such as `Blueprint.Runtime.Http.HttpNoContentResult`.
  - The first approved separation rule stays intentionally narrow: it protects the semantic boundary between host-neutral and host-owned no-body success shapes, while unsupported members remain owned by `BP108` and same-status body/no-body unions remain owned by `BP110`.
  - The proof matrix now includes a red-to-green descriptor diagnostic test file (`OperationDescriptorSourceGeneratorTests.NoResultShapeSeparation.cs`) covering both member orders, clean single-result regressions, and explicit `BP108`/`BP110` boundary cases, plus a green targeted `Blueprint.SourceGenerator.Tests` run.
- Completed the first key-only resource-event HTTP mapping slice:
  - `Blueprint.Runtime.Exec.KeyOnlyResourceEventResult` now defines the first host-neutral semantic resource-event shape for event/resource identity without a response body, carrying stable `EventId` and `ResourceId` values while remaining distinct from both `NoResultOperationResult` and host-owned HTTP no-content results.
  - `Blueprint.Runtime.Http.HttpOperationResultExecutor` now translates that semantic result to empty HTTP `201 Created` responses by convention, and the shared response-shape helper threads that same mapping through `HostExecutorAvailabilityModule`, `AmbiguousHostTranslationModule`, and `HttpUnionDiscriminationStrategyModule`.
  - The proof matrix now includes a red-to-green descriptor support test, a red-to-green endpoint translation test, a flattened descriptor-outcome proof, and a same-status boundary test proving `Results<KeyOnlyResourceEventResult, NoResultOperationResult>` no longer triggers `BP110`.
- Approved the first semantic result-family cut:
  - Human direction now locks `CreatedResult<T>`, `AcceptedResult` / `AcceptedResult<T>`, and `ConflictResult` into the first implementation pass instead of deferring any of those families to a later phase.
  - Even with that broader pass-1 scope, execution stays intentionally incremental: land those families as separate narrow slices, starting with `CreatedResult<T>` so the body-bearing `201 Created` path extends the already-proven `KeyOnlyResourceEventResult` precedent.
- Completed the first body-bearing created-result slice:
  - `Blueprint.Runtime.Exec.CreatedResult` / `CreatedResult<T>` now provide the first host-neutral "resource created with payload" semantic result family, keeping the body-bearing `201 Created` path distinct from both `OkResult<T>` (`200` JSON) and `KeyOnlyResourceEventResult` (`201` empty).
  - `Blueprint.Runtime.Http.HttpOperationResultExecutor` now translates that semantic family to JSON `201 Created` responses, and the shared response-shape helper threads the same mapping through `HostExecutorAvailabilityModule`, `AmbiguousHostTranslationModule`, and `HttpUnionDiscriminationStrategyModule`.
  - The proof matrix now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, a status-boundary proof showing `Results<CreatedResult<string>, OkResult<string>>` stays clear of both `BP109` and `BP110`, and an explicit same-status regression proving `Results<CreatedResult<string>, KeyOnlyResourceEventResult>` still triggers `BP110` until a later discriminator slice deliberately widens that rule.
- Completed the accepted-result asynchronous success slice:
  - `Blueprint.Runtime.Exec.AcceptedResult` / `AcceptedResult<T>` now provide the next host-neutral "accepted but not yet completed" semantic success family, keeping the body-bearing `202 Accepted` path distinct from immediate `OkResult<T>` success (`200`) and body-bearing created-resource success (`201`).
  - `Blueprint.Runtime.Http.HttpOperationResultExecutor` now translates that semantic family to JSON `202 Accepted` responses, and the shared response-shape helper threads the same mapping through `HostExecutorAvailabilityModule`, `AmbiguousHostTranslationModule`, and `HttpUnionDiscriminationStrategyModule`.
  - The proof matrix now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, and a status-boundary proof showing `Results<AcceptedResult<string>, OkResult<string>>` stays clear of both `BP109` and `BP110`.
- Completed the conflict-result semantic conflict slice:
  - `Blueprint.Runtime.Exec.ConflictResult` / `ConflictResult<T>` now provide the final host-neutral semantic family in the currently approved first result-family cut, keeping the body-bearing `409 Conflict` path distinct from the `200`, `201`, and `202` success families.
  - `Blueprint.Runtime.Http.HttpOperationResultExecutor` now translates that semantic family to JSON `409 Conflict` responses, and the shared response-shape helper threads the same mapping through `HostExecutorAvailabilityModule`, `AmbiguousHostTranslationModule`, and `HttpUnionDiscriminationStrategyModule`.
  - The proof matrix now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, and a status-boundary proof showing `Results<ConflictResult<string>, OkResult<string>>` stays clear of both `BP109` and `BP110`.
- Review gate after the first semantic result-family cut:
  - The currently approved family cut is now complete: `CreatedResult<T>`, `AcceptedResult` / `AcceptedResult<T>`, and `ConflictResult` are all landed.
  - Neither `migration\plan.md` nor this workstream plan explicitly approves a next slice after Conflict, so `migration\progress.json` should stop with `lastCompletedSlice = conflict-result-first-pass-support`, `nextReadySlice = null`, and an open human-direction review gate instead of fabricating another follow-on.
- Cleared the post-Conflict review gate by later human direction:
  - The human explicitly delegated next-slice selection to this session, so the next approved follow-on is `NotFoundResult` / `NotFoundResult<T>` as the next semantic missing-resource result family with default HTTP `404 Not Found` mapping.
  - `NotFoundResult` is already part of the canonical shared semantic taxonomy in this workstream, and it extends the same distinct-status result-family pattern proven by the `CreatedResult<T>`, `AcceptedResult`, and `ConflictResult` slices without widening into host-specific status-only families or the separate exception/error workstream yet.
- Completed the NotFound-result semantic missing-resource slice:
  - `Blueprint.Runtime.Exec.NotFoundResult` / `NotFoundResult<T>` now provide the next host-neutral semantic family for missing-resource outcomes, and the current HTTP host maps that family to body-bearing JSON `404 Not Found` responses without collapsing it into success or conflict semantics.
  - The shared response-shape helper now threads that `404 + body` mapping through `HostExecutorAvailabilityModule`, `AmbiguousHostTranslationModule`, and `HttpUnionDiscriminationStrategyModule`, keeping descriptor-time validation aligned with runtime translation.
  - The proof matrix now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, and a clean status-boundary proof showing `Results<NotFoundResult<string>, OkResult<string>>` stays clear of both `BP109` and `BP110`.
- Review gate after the NotFound-result slice:
  - `NotFoundResult` is now landed, but this workstream plan still does not record an explicit post-NotFound follow-on.
  - If execution stops here, `migration\progress.json` should reopen a human-direction gate rather than guessing whether the next safe move is another semantic result slice or a pivot into the paired error-model workstream.
- Cleared the post-NotFound review gate by later session judgment:
  - The next approved follow-on remains inside this workstream: introduce canonical `ValidationFailedResult` and treat legacy `ValidationFailedOperationResult` as a compatibility alias or shim rather than leaving validation failures on a legacy-only surface.
  - That follow-on is explicitly grounded in this plan's canonical taxonomy (`ValidationFailedResult`) and legacy-alias guidance (`ValidationFailedOperationResult`-style names remain compatibility aliases where needed), while still preserving the current unsupported-host boundary until the broader error-model workstream deliberately widens host translation for failure results.
- Completed the ValidationFailedResult canonicalization slice:
  - `Blueprint.Runtime.Exec.ValidationFailedResult` now closes the canonical semantic name gap for known input-contract failures, and `Blueprint.Middleware.ValidationFailedOperationResult` remains available only as a compatibility alias inheriting from that canonical type.
  - Generator-owned validation short-circuits and validation-exception catch translation now emit the canonical result/helper names, but HTTP-visible contracts using validation-failure results still intentionally remain outside the supported host-executor surface and continue to report `BP108`.
  - The proof matrix now includes a red-to-green generated validation pipeline proof, a red-to-green canonical `BP108` boundary proof, a green `Blueprint.SourceGenerator.Tests` project run (`275` tests), a green `Blueprint.SourceGenerator.Tests.Integration` run (`55` tests), and a green `dotnet build .\Blueprint.sln --no-restore`.
- Next approved follow-on after ValidationFailedResult canonicalization:
  - Stay in this workstream long enough to introduce the still-planned internal canonical `UnhandledExceptionResult` shape while treating `UnhandledExceptionOperationResult` as the compatibility/internal legacy name.
  - That remains the safest next step because this plan still explicitly requires an internal unhandled-exception result shape in the shared taxonomy, and `migration\error-model-and-exception-handling\plan.md` depends on a locked result model plus a generic unhandled-exception fallback.
- Completed the UnhandledExceptionResult canonicalization slice:
  - `Blueprint.Runtime.Exec.UnhandledExceptionResult` now provides the canonical internal fallback shell, while `Blueprint.UnhandledExceptionOperationResult` remains available only as a compatibility alias inheriting from it.
  - `PipelineCodeGenerator` now emits the canonical outer-catch result name, and `OperationResultContractHelper` plus `FlattenedResultOutcomeModule` keep both direct and `Results<T1, T2>` contracts that reference either unhandled-exception name on the `BP111` side of the public-contract boundary.
  - The proof matrix now includes a red-to-green generated pipeline-source proof, direct and union `BP111` proofs for both canonical and legacy names, a runtime integration proof for thrown handlers, a green `Blueprint.SourceGenerator.Tests` run (`280` tests), a green `Blueprint.SourceGenerator.Tests.Integration` run (`56` tests), a green `dotnet build .\Blueprint.sln --no-restore`, and clean independent review.
- Approved handoff after UnhandledExceptionResult canonicalization:
  - Pivot to `migration\error-model-and-exception-handling\plan.md` with a narrow first slice that extracts a shared exception-translator seam around the current executor shell while preserving the existing `ApiException` special handling and generic unhandled fallback.
  - That handoff is now safer than more Workstream 7 taxonomy work because this workstream's canonical result surface is locked enough for failure modeling, while the remaining catch/translation policy belongs to the error-model workstream's explicit sequencing.
- Later session judgment for OpenAPI completion:
  - temporarily override the earlier error-model handoff and return to this workstream for `result-model-and-host-translation/introduce-body-bearing-resource-event-results`
  - this is the narrowest dependency-safe prerequisite because OpenAPI's remaining same-status discriminator carve-out depends on body-bearing resource-event outcomes with a stable serialized `eventId`, and that result contract does not exist yet
  - keep the slice intentionally narrow: introduce the canonical body-bearing resource-event result shape and defer broader discriminator emission, grouped-event unions, and other OpenAPI rendering work to the follow-on slices that consume it
- Completed the body-bearing resource-event prerequisite slice:
  - `Blueprint.Runtime.Exec.ResourceEventResult` / `ResourceEventResult<T>` now provide the canonical host-neutral body-bearing resource-event semantic family, carrying stable `EventId`, `ResourceId`, and a typed payload while remaining distinct from both `CreatedResult<T>` and the empty-body `KeyOnlyResourceEventResult` path
  - `HttpOperationResultExecutor` now translates that semantic family to JSON `201 Created` envelopes with top-level `eventId`, `resourceId`, and `content`, and the descriptor helper surface now treats that family as supported by the current HTTP executor and flattened outcome projection
  - the serializer fallback path now keeps app-owned source-generated metadata first but can still serialize uncovered wrapper roots through a reflection fallback while the broader serializer-coverage migration remains in progress
- Proof closed for this slice:
  - red-to-green endpoint translation proof in `OperationResultTranslationEndpointTests`
  - red-to-green descriptor support proof in `OperationDescriptorSourceGeneratorTests.HostExecutorAvailability`
  - red-to-green flattened-outcome proof in `OperationDescriptorSourceGeneratorTests.FlattenedOutcomes`
  - green `Blueprint.SourceGenerator.Tests` project run
- Next handoff back to OpenAPI:
  - return the live tracker to `migration\openapi\plan.md`
  - continue with `openapi/allow-same-status-body-bearing-resource-event-unions-with-eventid-discriminator`
  - that is now the narrowest dependency-safe follow-on because the prerequisite semantic result family exists, the HTTP body now carries a stable serialized `eventId`, and the remaining work is to let descriptor/OpenAPI discrimination consume that fact without widening other same-status unions accidentally
- Follow-up correction after implementation review:
  - the new runtime envelope now carries a stable serialized `eventId`, but the generator still has no compile-time source for that value from a declared `ResourceEventResult<T>` contract alone
  - before OpenAPI can safely auto-discriminate same-status body-bearing resource-event unions, this workstream therefore needs one more narrow prerequisite: `result-model-and-host-translation/expose-compile-time-resource-event-discriminator-metadata`
  - keep that slice intentionally narrow: add a compile-time-stable event-id declaration seam for body-bearing resource-event outcomes, then return to the OpenAPI discriminator slice once descriptor planning can prove each union member's `eventId`
- Completed the compile-time resource-event discriminator metadata prerequisite:
  - consumers can now declare `[ResourceEvent("...")]` on the payload type used by `Blueprint.Runtime.Exec.ResourceEventResult<T>` to provide the stable compile-time `eventId` contract that later descriptor and OpenAPI slices can inspect
  - descriptor generation now projects that proof into `OperationResourceEventMetadataFeatureData`, carrying the semantic result type name, payload type name, and stable `eventId` for each body-bearing resource-event outcome it can prove from the declared contract
  - the slice stays intentionally narrow: it does not yet widen same-status union discrimination or OpenAPI rendering, it only exposes the compile-time source of truth those follow-on slices need
- Proof closed for this slice:
  - red-to-green descriptor proof that `[ResourceEvent("events.created")]` on a `ResourceEventResult<T>` payload emits `OperationResourceEventMetadataFeatureData`
  - negative descriptor proof that the same contract without `[ResourceEvent]` does not emit resource-event metadata
- Handoff after this prerequisite:
  - return the live tracker to `migration\openapi\plan.md`
  - resume `openapi/allow-same-status-body-bearing-resource-event-unions-with-eventid-discriminator`
  - that is now the narrowest dependency-safe follow-on because the generator can finally prove per-member `eventId` metadata from the declared body-bearing resource-event contracts instead of inferring only from runtime constructor arguments
