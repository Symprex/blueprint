# Pipeline composition and execution

- Status: Planned
- Priority: P1
- Classification: Core
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`

## Why this module exists

The legacy Blueprint library generated executor pipelines by composing ordered middleware contributions plus handler-execution builders. The current rewrite generates a single monolithic `ExecuteAsync` body with fixed logging, tracing, DI, and exception handling.

To migrate real functionality, the rewrite needs a composable execution plan rather than more hard-coded branches in `PipelineCodeGenerator`.

## Evidence

- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\HandlerDiscovery.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\ParameterAnalyzer.cs`
- Current runtime contract: `src\Blueprint.Runtime\Exec\IOperationExecutorPipeline.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\OperationExecutorMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\IOperationExecutorBuilderScanner.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\CodeGennedExecutor.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandler.cs`
- Legacy generated output: `D:\Projects\Signature365\src\Apps\AdminPortal\Symprex.Signature365.AdminPortal\Internal\Generated\Blueprint\AdminPortalCreateLicenseKeyCommandExecutorPipeline.cs`
- Signature365 extension evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Api\OperationsHandlerScanner.cs`

## Migration goal

Generate pipelines from a `PipelinePlan` that can combine:

- ordered middleware contributions
- handler discovery and invocation plans
- result normalization and transformation
- exception translation
- a shared executor shell
- post-execution behavior

## Runtime support to build

- Generated pipeline types and app executors
- A stable shared executor shell/runtime helper for activity, logging, exception translation, and cleanup
- Host-agnostic result normalization so handler returns become semantic result values rather than immediately executed host results
- Handler invocation support for:
  - inline methods
  - external handler classes
  - core grouped-handler conventions such as `ApiOperations`
  - optional multi-handler semantics where allowed
  - optional alternate handler sources/scanners

The runtime should remain thin; most execution shape should already be decided at generation time.

The result model should also become host agnostic. Generated pipelines should produce semantic result values; host packages such as HTTP then execute or translate those values.

## Shared executor shell

Every generated operation currently repeats the same broad shape: create activity/logging scope, run staged logic, translate failures, and clean up. The new design should stop treating that as duplicated per-operation boilerplate.

Plan for a two-layer execution shape:

1. a shared executor shell that owns cross-cutting scaffolding
2. generated per-operation core code that plugs in handler invocation, validation, auth checks, result shaping, and post-processing

This should reduce duplication, make exception behavior consistent, and give auditing/observability/error-model modules one clear place to participate.

## Runtime executor contract

The shared executor shell is an implementation strategy, not the public runtime surface.

Planning decision:

- generated pipelines should continue to satisfy a stable runtime execution contract equivalent to the current `IOperationExecutorPipeline`
- tests, programmatic execution, HTTP dispatch, and future non-HTTP hosts should all target that stable contract
- the shared executor shell sits *under* that contract and should not force consumers to learn a second public abstraction just to execute an operation
- the pipeline should not hard-code `new OkResult(handlerResult)` as the universal outcome shape; it should preserve explicit result types, normalize plain values into semantic success results, and leave host execution to the active host

## Replacing general nested execution

Legacy Blueprint has an explicit top-level versus nested split, but the concrete trigger we found is `ResourceEventHandler` creating a nested context, setting `SkipAuthorisation = true`, and re-running execution for a self-query.

The plan should therefore assume:

- general-purpose `ExecuteNestedAsync` parity is **not** the target
- the first design move is to simplify or remove resource-event self-query where possible
- if a re-query path still exists, it should be a narrow internal facility used by resource-event post-processing, not a second universal execution mode that every pipeline contributor must understand

This preserves behavior where it matters without carrying the full legacy dual-path model forward unnecessarily.

## Re-query decision gate

This question must be closed before Phase 2 resource-model implementation begins.

Decision process:

1. review the concrete Signature365 resource-event scenarios that currently depend on self-query
2. prove whether handler output plus generated post-processing can supply the required event payload and links directly
3. only if that fails for required scenarios, bless the narrow internal re-query path as the implementation strategy

Exit criteria:

- if the required scenarios can be satisfied without re-query, resource events proceed with no internal re-query facility
- if at least one required scenario cannot be satisfied without either breaking the consumer contract excessively or reintroducing worse runtime complexity elsewhere, the narrow internal re-query facility becomes the approved path

## Consumer configuration and activation

Support these patterns:

- default inline/external handler discovery
- core grouped-handler discovery via `ApiOperations` / `OperationsHandlerScanner`
- explicit handler-source or scanner registration only for true consumer extension packages
- clear rules for operations that may have one handler versus multiple handlers
- future package-provided execution contributors without patching the core generator

## Compile-time checks to add

- no handler found
- too many handlers for single-handler operations
- unsupported handler signature
- unsupported parameter source
- conflicting execution contributors
- illegal ordering around the execution stage
- attempted internal re-query on an unsupported operation/host shape

## Error and exception handling

Exception handling must move out of the "hard-coded catch blocks" category and into the pipeline plan.

The execution plan should support:

- declared failure outcomes for an operation
- ordered exception translators
- non-error exception/result translations such as validation failures
- host-aware mapping from exception type to runtime result
- integration with the shared executor shell
- linkage to OpenAPI/response documentation

The current catch blocks in `PipelineCodeGenerator` are a starting point, not the final architecture.

## Observability and debuggability

Expose:

- handler source chosen for each operation
- stage plan for each generated pipeline
- shared executor shell policy
- exception-policy summary
- variable/service resolution summary

Generated code comments are useful, but a structured plan/manifest is even better.

## Explicit decisions

- Do not port `IOperationExecutorBuilder` + frames literally.
- Preserve the ability for packages to contribute alternate handler discovery and execution behavior.
- Keep `CodeWriter` as the final renderer, not the planning model.
- Preserve behavior compatibility even if the generated C# shape changes radically from the legacy output.
- Introduce a common executor shell so cross-cutting scaffolding is shared instead of duplicated.
- Treat general nested execution parity as out of scope unless a narrower internal re-query path proves insufficient.
- Treat `ApiOperations` / `OperationsHandlerScanner` behavior as core pipeline discovery functionality.
- Make result handling data-first and host agnostic. Pipelines produce semantic result values; hosts execute them.

## Sequencing

This is one of the first implementation modules. Validation, auth, HTTP, resources, tasks, persistence, and messaging all depend on it.
