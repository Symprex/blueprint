# Result model and host translation

- Status: Planned
- Priority: P1
- Classification: Core/result
- Depends on: `../foundation-module-system/plan.md`, `../pipeline-composition-and-execution/plan.md`

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
- Dependent module plan: `../resource-model-links-and-post-processing/plan.md`

## Migration goal

Redesign Blueprint results so they express **semantic outcomes**, while hosts such as HTTP translate those outcomes into status codes, headers, bodies, and other transport behavior.

That means:

- typed success results remain available
- failure results remain explicit
- typed unions (`Results<T1, ...>`) become part of the public contract surface
- resource-event results become concrete contract-level result types
- HTTP semantics travel as attachable hints rather than as hard-coded execution behavior on the result classes themselves

## Runtime support to build

- A shared host-agnostic result abstraction in the contract-visible model/abstractions layer
- Typed success-result shapes such as `OkResult<T>` or a close equivalent
- Typed one-of-many unions (`Results<T1, ...>`) for operation contracts and handler returns
- Semantic failure-result shapes used by exception translation and validation
- A transport-neutral hint model that any result can carry
- Host executors/adapters that translate semantic results plus hints into transport behavior
- Generator/runtime support for normalizing plain handler return values into semantic result shapes

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

### 3. Typed unions are part of the public result contract

Blueprint should add a host-agnostic `Results<T1, ...>` union model inspired by Minimal APIs.

That union becomes the canonical way to declare one-of-many allowed outcomes in operation contracts.

### 4. Host hints are attachable metadata, not inheritance branches

HTTP-specific behavior such as:

- `ETag`
- cache control
- explicit headers
- content-negotiation hints

should be expressed as attachable hints on any result.

Illustrative direction:

```csharp
return new OkResult<TemplateApiResource>(resource)
    .WithHttpETag(resource.ETag)
    .WithHttpCacheControlPublic(TimeSpan.FromMinutes(5));
```

The result stays host agnostic; the HTTP host decides how to interpret the hints.

### 5. Resource-event results build on this model rather than reinventing it

Resource-event result types belong to the resource-model module, but they must sit on top of this shared result architecture:

- typed one-of-many outcomes
- semantic result shapes
- host-neutral execution
- attachable HTTP hints

The resource-model plan should not own a second independent result system.

## Compile-time checks to add

- `Results<T1, ...>` may only include supported semantic result types
- handlers must return a type compatible with the declared union or declared success shape
- conflicting or unsupported host hints for a declared host/result combination
- explicit result type declared but no host executor/adapter exists for an enabled host
- semantic result shapes that would be ambiguous to translate for a declared host

## Observability and debuggability

- generated per-operation result-contract manifest
- host translation summary showing which executor/adapter handles each result shape
- diagnostics that explain why a result type or host hint is unsupported
- explicit mapping from semantic result shapes to HTTP behavior for HTTP-enabled operations

## Explicit decisions

- Keep the shared result abstraction host agnostic.
- Keep typed success results such as `OkResult<T>` or a close equivalent.
- Make `Results<T1, ...>` part of the public contract model.
- Express HTTP-specific behavior through attachable hints rather than by embedding HTTP execution into result types.
- Let hosts execute/translate results; result types themselves should not own transport execution.
- Let resource-event results build on this shared result model instead of defining a separate parallel outcome system.

## Sequencing

Implement after the pipeline plan is stable enough to define result normalization, but before HTTP, error handling, OpenAPI, and the full resource-event model depend on a stable result contract.
