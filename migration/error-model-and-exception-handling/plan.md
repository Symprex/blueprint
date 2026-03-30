# Error model and exception handling

- Status: Planned
- Priority: P1
- Classification: Core
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`, `../result-model-and-host-translation/plan.md`

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

Make failure outcomes part of the compile-time contract so the shared executor shell, host result handling, and OpenAPI generation all work from the same declared error model.

## Runtime support to build

- shared executor-shell exception boundaries that invoke pipeline exception translators deterministically
- pipeline exception translators that map known exception families to operation results
- a descriptor-level failure metadata model
- host-aware mapping from semantic failure result type to HTTP status and response body
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
- Keep failure results host agnostic. HTTP status and response-body translation belong to the active host, not to the result type itself.

## Sequencing

Implement alongside the core pipeline planning work, before OpenAPI and before broad host parity work depends on complete response contracts.

## Approved follow-on after Workstream 7 result-model lock

- Start with a narrow seam-extraction slice inside the generated executor shell:
  - introduce a shared exception-translator seam that can preserve today's hard-coded `ApiException` non-5xx handling plus the generic unhandled-exception fallback without changing public failure taxonomy yet
  - keep host behavior, documented failure contracts, and OpenAPI response projection out of this first slice
- The acceptance target for that first slice is replaceable exception-translation structure, not new public failure-result families.
- Completed the narrow executor-shell seam-extraction slice:
  - `PipelineCodeGenerator` now routes generated catch-block emission through `IPipelineExceptionTranslator`, and `DefaultPipelineExceptionTranslator` preserves the current `Blueprint.Validation.ValidationException`, `System.ComponentModel.DataAnnotations.ValidationException`, non-5xx `ApiException`, and generic unhandled fallback behavior without widening the public result taxonomy.
  - The proof matrix now includes a red-to-green direct codegen seam-replacement test, unchanged generated-source checks for canonical validation and unhandled fallback names, a green runtime integration proof for thrown handlers, green `dotnet format .\Blueprint.sln --no-restore`, a green solution build, and green `Blueprint.SourceGenerator.Tests`, `Blueprint.SourceGenerator.CodeWriter.Tests`, and `Blueprint.SourceGenerator.Tests.Integration` runs.
- Autonomous follow-on after seam extraction:
  - Implement ordered exception translator policy with compile-time conflict detection.
  - That is the safest next slice because the new `IPipelineExceptionTranslator` seam is now pluggable, but the repository still needs one deterministic policy for translator precedence and overlap before it can safely add descriptor-level failure metadata, host-aware mapping, or package-provided translator seams.
  - Acceptance target for this next slice:
    - define how multiple exception translators are ordered and applied
    - add compile-time conflict detection for overlapping translator ownership
    - keep host-specific mapping and public failure-taxonomy expansion deferred
    - prove the ordering behavior with focused generator and integration coverage before widening the contract further
- Completed the ordered exception-translator policy slice:
  - `PipelineExceptionTranslatorPolicy.CreateDefault()` now validates and orders the default translator set before `PipelineCodeGenerator` emits the outer catch shell.
  - The default policy keeps `LegacyValidationExceptionTranslator`, `DataAnnotationsValidationExceptionTranslator`, and the catch-all `DefaultPipelineExceptionTranslator` explicit, preserves the previous validation and `ApiException` behavior, and emits the fallback translator last.
  - `BP113` now rejects overlapping non-fallback translator ownership so exception translation does not silently depend on incidental registration order.
  - The proof matrix now includes red-to-green policy ordering/conflict tests, a direct codegen catch-order proof, targeted integration proofs for `ApiException` and validation-exception paths, green `dotnet format .\Blueprint.sln --no-restore`, a green `dotnet build .\Blueprint.sln --no-restore`, and green `Blueprint.SourceGenerator.Tests` (`285`), `Blueprint.SourceGenerator.CodeWriter.Tests` (`45`), and `Blueprint.SourceGenerator.Tests.Integration` (`58`) runs.
- Autonomous follow-on after ordered translator policy:
  - Introduce descriptor-level failure metadata model aligned with ordered translator ownership.
  - That is the narrowest safe next slice because translator precedence and overlap are now explicit, so descriptor generation can project failure facts without guessing which translator owns an exception family.
  - Acceptance target for this next slice:
    - record descriptor-level failure metadata for known translator-owned exception families
    - keep host-aware status/body mapping and package-provided translator activation deferred
    - prove metadata projection and compile-time validation before widening host or OpenAPI consumers
- Completed the descriptor-level failure metadata slice:
  - `FailureOutcomesModule` now projects ordered `OperationFailureOutcomesFeatureData` onto generated descriptors using the validated default translator policy, so descriptor consumers can inspect known failure families without reverse-engineering generated catch blocks.
  - The new `OperationFailureOutcome` contract currently carries the stable translator identifier, handled exception type name, and translated result type name as strings; this keeps the slice host agnostic and preserves contract-only descriptor generation without forcing runtime-only failure CLR types into the contract yet.
  - The proof matrix now includes red-to-green descriptor-generator assertions for emitted failure metadata, a contract-only descriptor proof that the string-based feature data survives without `Blueprint.OperationResult`, a green runtime `Configure()` integration proof for ordered failure metadata lookup, and the broader verification sweep captured with this slice before widening into host-aware mapping.
- Autonomous follow-on after descriptor-level failure metadata:
  - Introduce host-aware mapping from descriptor failure metadata to HTTP status and response-body conventions.
  - That is the narrowest safe next slice because failure ownership is now explicit at descriptor time, which gives host translation one compile-time source of truth without yet widening into package-provided translators or OpenAPI projection.
  - Acceptance target for this next slice:
    - map the known failure result families surfaced by descriptor metadata to the current HTTP host response model
    - keep package-provided translator activation and OpenAPI response projection deferred
    - prove host-aware failure translation from descriptor metadata before widening the public failure contract further
- Completed the host-aware failure mapping slice:
  - `HttpOperationResultExecutor` now reads `OperationFailureOutcomesFeatureData` from the current descriptor and translates matching `ValidationFailedResult` families to `422 Unprocessable Entity` problem responses plus matching `UnhandledExceptionResult` families to `500 Internal Server Error` problem responses, while descriptors that do not advertise the relevant failure outcome still fall back to the legacy self-executing `OperationResult.ExecuteAsync(...)` path.
  - `OperationResultContractHelper`, `HostExecutorAvailabilityModule`, `AmbiguousHostTranslationModule`, and `HttpUnionDiscriminationStrategyModule` now treat declared `ValidationFailedResult` contracts as part of the supported HTTP executor surface, so `BP108`, `BP109`, `BP110`, and `BP112` reason from the widened `422`-with-body response shape without widening the internal-only unhandled fallback contract.
  - The proof matrix now includes red-to-green descriptor diagnostics for declared validation-failure contracts and their HTTP-visible unions, red-to-green endpoint translation tests that prove descriptor-gated `422` / `500` problem responses plus the metadata-missing fallback path, and the broader verification sweep captured with this slice after the runtime host mapping landed.
- Autonomous follow-on after host-aware failure mapping:
  - Activate package-provided exception translators through the module system.
  - That is the narrowest safe next slice because the default translator policy, descriptor failure metadata, and host-aware HTTP mapping now agree on one failure contract; the remaining in-workstream gap is letting packages contribute new translators without bypassing ordering/conflict validation or fragmenting descriptor and host behavior.
  - Acceptance target for this next slice:
    - activate package-provided exception translators while reusing `PipelineExceptionTranslatorPolicy` ordering and conflict detection
    - project package-contributed failure ownership through the same descriptor failure metadata contract that the HTTP host now consumes
    - keep OpenAPI response projection deferred until the later OpenAPI workstream consumes the richer failure contract deliberately
- Completed the package-provided exception translator activation slice:
  - `BlueprintSourceGenerationConfig.BuildExecutionPlan()` now includes ordered `IExceptionTranslationContributor`s, `DefaultExceptionTranslationModule` contributes the built-in translator set through that capability, and both descriptor and pipeline generators create `PipelineExceptionTranslatorPolicy` from the same contributor-composed execution plan instead of recreating hard-coded defaults independently.
  - `OperationDescriptorSourceGenerator` and `OperationExecutorPipelineSourceGenerator` now expose protected configuration seams so companion analyzer packages can append modules onto Blueprint's default module set, while `FailureOutcomesModule` now projects package-contributed exception ownership through the same `OperationFailureOutcomesFeatureData` contract that the HTTP host already consumes.
  - The proof matrix now includes red-to-green execution-plan, descriptor, and pipeline package-activation tests; a full generated-pipeline snapshot for the package-translator catch order; a package-conflict `BP113` proof that reuses the shared policy diagnostics; and a green broader verification sweep across `dotnet format .\Blueprint.sln --no-restore` (with the repo's known IDE1006 Fix All warnings), `dotnet build .\Blueprint.sln --no-restore` (`0` warnings / `0` errors), `Blueprint.SourceGenerator.Tests` (`294`), `Blueprint.SourceGenerator.CodeWriter.Tests` (`45`), and `Blueprint.SourceGenerator.Tests.Integration` (`59`). The synthetic build-telemetry baselines now also intentionally record the new `ExceptionTranslationPolicy` stage for descriptor generation.
  - A true runtime integration proof for a package-owned analyzer remains deferred because `Blueprint.SourceGenerator.Tests.Integration` currently references the default analyzer directly; the package path is therefore locked through compile-time generator-composition proofs until a dedicated analyzer-fixture harness becomes worth the added maintenance cost.
- Autonomous follow-on after package-provided exception translator activation:
  - Pivot into the OpenAPI workstream with a narrow first slice: project descriptor-provided failure outcomes into generated HTTP response metadata.
  - That is the narrowest dependency-safe next slice because Workstream 8 now leaves one stable compile-time source of truth for built-in and package-provided failure families, and the OpenAPI plan explicitly expects cross-cutting error outcomes to merge with operation-specific response metadata.
  - Acceptance target for this next slice:
    - consume `OperationFailureOutcomesFeatureData` alongside flattened success outcomes when building generated HTTP response metadata
    - surface validation and unhandled fallback families in generated HTTP response descriptions without rediscovering exception policy at runtime
    - keep richer schema/discriminator work deferred until later OpenAPI slices widen response/body shaping deliberately
