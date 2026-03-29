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
- Require distinct HTTP status codes by default for HTTP-visible unions; same-status unions require explicit discriminator opt-in.
- Allow body-bearing resource-event unions to auto-satisfy the same-status rule through a generated serialized `eventId` discriminator; key-only/no-body event outcomes are not part of that carve-out.
- Treat legacy `ValidationFailedOperationResult`-style names as compatibility aliases where needed, but converge the semantic model on canonical data-first result names.

## Open questions to resolve

- How should `IQuery<T>` / `ICommand<T>` / `IReturn<T>` metadata interact with `Results<T1, ...>` so descriptor generation and OpenAPI/client generation see the expanded contract rather than a raw wrapper type?
- Which semantic result families must land in the first implementation pass versus a second pass (`CreatedResult<T>`, `AcceptedResult<T>`, `ConflictResult`)?
- What is the default HTTP status mapping for host-neutral `NoResult`, and how should that interact with explicit host-specific no-content/status-only result families?
- What is the default HTTP status mapping for key-only resource-event results, and how much of that mapping should be convention versus explicit metadata?

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
- Next approved follow-on: add the first compile-time diagnostic ensuring HTTP-visible unions have an unambiguous host/client discrimination strategy.
