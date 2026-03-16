# Error model and exception handling

- Status: Planned
- Priority: P1
- Classification: Core
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`

## Why this module exists

Legacy Blueprint had a richer error story than the current rewrite documents explicitly:

- `ApiException`-style failures with HTTP status implications
- exception-to-response conventions
- response documentation influenced by known exception outcomes
- pipeline-level exception translation

The current rewrite only hard-codes a few catch blocks in the generated pipeline.

## Evidence

- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\ApiExceptionFactoryResponseConvention.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Errors\`
- Legacy generated output: exception handling in generated `*ExecutorPipeline.cs` files

## Migration goal

Make failure outcomes part of the compile-time contract so the shared executor shell, HTTP result handling, and OpenAPI generation all work from the same declared error model.

## Runtime support to build

- shared executor-shell exception boundaries that invoke pipeline exception translators deterministically
- pipeline exception translators that map known exception families to operation results
- a descriptor-level failure metadata model
- host-aware mapping from operation result/failure type to HTTP status and response body
- support for package-provided exception translators when needed

## Consumer configuration and activation

Support existing or replacement shapes for:

- `ApiException`-style failures
- custom exception factories or response conventions
- package-provided exception translation modules

Consumers should be able to declare failure semantics once and have execution, HTTP, and OpenAPI all observe them.

## Compile-time checks to add

- exception metadata declared but no translator/result mapping exists
- conflicting translators for the same exception family
- HTTP/OpenAPI host enabled without enough failure metadata to document important outcomes
- unsupported exception shapes that would otherwise fall back silently to generic 500 behavior

## Observability and debuggability

- generated failure manifest per operation
- explicit log/trace behavior for translated vs unhandled failures
- diagnostics that explain which module declared each failure outcome

## Explicit decisions

- Keep validation-failure translation as part of the broader exception/result model rather than a one-off special case.
- Treat documented failure outcomes as part of the public API contract.
- Keep a generic unhandled-exception fallback, but make the important known error paths explicit.
- Let the shared executor shell own the default try/catch/finally structure so per-operation code only expresses declared differences.

## Sequencing

Implement alongside the core pipeline planning work, before OpenAPI and before broad host parity work depends on complete response contracts.
