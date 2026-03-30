# Validation

- Status: In progress
- Priority: P1
- Classification: Core middleware
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`

## Why this module exists

Validation is part of Blueprint's normal execution contract, not an optional afterthought. The legacy library supports both DataAnnotations and Blueprint-specific validation attributes, and returns a validation-failure result rather than generic unhandled errors; the rewrite now canonicalizes that shared result shape as `ValidationFailedResult` while retaining `ValidationFailedOperationResult` as a compatibility alias.

This is also one of the clearest areas where the rewrite can improve by precomputing more validation behavior at compile time.

## Evidence

- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\ValidationMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\BlueprintValidationBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Validation\`
- Generated evidence: `D:\Projects\Signature365\src\Apps\AdminPortal\Symprex.Signature365.AdminPortal\Internal\Generated\Blueprint\AdminPortalCreateLicenseKeyCommandExecutorPipeline.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- Signature365 usage scan across `D:\Projects\Signature365\src`: `RequiredIf` (40), `EmailAddress` (13), `GreaterThan` (10), `RequiredIfNot` (9), `PhoneNumber` (6), `GreaterThanProperty` (5), `Url` (3), `NotAllowedIf` (3); other legacy Blueprint validation attributes were not found in the usage scan
- Consumer validation extensions: `ObjectStringLengthAttribute`, `SupportedRegionAttribute`, `SupportedCountryAttribute`, `ValidImageContentTypeAttribute`
- Options validation generator evidence: `D:\Projects\Signature365\src\Apps\EntraId\Symprex.Signature365.EntraId.Cli\Configuration\EntraIdConnectorOptions.cs`, `D:\Projects\Signature365\src\Apps\EntraId\Symprex.Signature365.EntraId.Cli\Configuration\Signature365EndpointsOptions.cs`

## Migration goal

Move to a DataAnnotations-first, source-generated validation model that keeps the important Blueprint-specific semantics where they add real value and trims the legacy validation surface everywhere else.

## Runtime support to build

- A generated property-validation plan per operation
- Generated DataAnnotations-style validation code, using the .NET options-validation generator approach as the reference pattern for what "generated validation instead of reflective validation" should look like
- A stable semantic validation-failure path, with `ValidationFailedResult` as the canonical name and `ValidationFailedOperationResult` available as a migration alias where needed while the shared result model converges on canonical data-first names
- Minimal runtime helpers only where truly required for async/contextual validators
- Optional validation-source registration for package-provided validators
- Compatibility handling for the retained Blueprint-specific validator subset

## Consumer configuration and activation

Treat validation attributes as declarative inputs and emit the validation plan they imply.

Long-term target:

- validation is always active when an operation carries validation attributes
- `.AddValidation()`, `.UseBlueprintSource()`, and `.UseDataAnnotationSource()` become migration shims or disappear
- explicit configuration remains only for custom validator sources/services that cannot be inferred
- generated options/configuration types should use the native options-validation generator directly where applicable

## Compile-time checks to add

- invalid cross-property references (for example comparison attributes referencing missing properties)
- unsupported validator placement or unsupported target types
- validators that would require runtime reflection but can be rejected or transformed at build time
- operation properties that cannot be validated as declared
- use of legacy compatibility attributes where a direct DataAnnotations replacement is preferred and can be code-fixed automatically

## Usage-driven retention plan

Keep as first-class generated Blueprint-specific validators because they add semantics not covered well by plain DataAnnotations:

- `RequiredIf`
- `RequiredIfNot`
- `GreaterThan`
- `GreaterThanProperty`
- `NotAllowedIf`

Treat as compatibility aliases or analyzer-guided migrations rather than strategic long-term Blueprint APIs:

- `EmailAddress`
- `PhoneNumber`
- `Url`

Drop from core unless new evidence appears during migration:

- `NotEmptyList`
- `MaxItemsList`
- `InPast`
- `InFuture`
- `NotInPast`
- `NotInFuture`
- `MustBeTrue`
- `GreaterThanOrEqualTo`
- `LessThan`
- `LessThanOrEqualTo`
- `MustBeGreaterThanOrEqual`

Continue to support consumer-authored `ValidationAttribute` types as a normal extensibility path rather than trying to funnel everything through Blueprint-owned attributes.

## Observability and debuggability

- generated validation summary per operation
- property-level failure source information
- deterministic diagnostics for invalid validation setups
- optional manifest of validators included in the generated pipeline

## Explicit decisions

- Favor generated validation code or generated validation descriptors over runtime reflection.
- Make DataAnnotations the primary validation contract for the rewrite.
- Keep the validation result contract compatible even if the generated implementation changes.
- Use the .NET options-validation generator as both a direct tool for configuration/options types and the reference design for Blueprint's generated validation style.
- Keep only a focused Blueprint-specific validator subset where it adds clear semantics beyond DataAnnotations.
- Treat Blueprint-specific validators that depend on request/user context as supported, but isolate the runtime helper surface they need.
- Land the retained Blueprint-specific conditional/cross-property validators before replacing the current exception-based validation-failure control flow.
- Treat JetBrains annotations as a repo-wide hygiene track; start with touched validation seams and capture the policy in a repo-local annotation-hygiene skill.

## Sequencing

Implement as part of the first parity wave, alongside execution and auth.

## Current migration status

- Property-level validation planning and the shared runtime fallback seam are already active for built-in validation families such as `Required`, `MaxLength`, `MinLength`, `StringLength`, and `EmailAddress`.
- The first retained Blueprint-specific cross-property validator is now active: `GreaterThanProperty` has a generated fast path for supported same-type non-nullable numeric/decimal/`DateTime` comparisons and falls back to `ValidationRuntime.ValidateProperty(...)` for unsupported shapes.
- The next retained Blueprint-specific value-comparison validator is now active: `GreaterThan` is available again as a consumer-facing attribute, has a generated fast path for non-nullable numeric/decimal properties with the default constructor shape, and falls back to `ValidationRuntime.ValidateProperty(...)` for display-name shaping and other unsupported/customized shapes.
- `BPV002` now covers missing referenced-property names for `GreaterThanPropertyAttribute`, extending the existing build-time validation pattern used for `CompareAttribute`.
- The current `ShortRun` benchmark evidence shows `GreaterThanPropertyInvalidAsync` at about `2.6 us` / `1.91 KB` versus `GreaterThanPropertyRuntimeFallbackInvalidAsync` at about `3.9 us` / `2.84 KB`, and `GreaterThanInvalidAsync` at about `1.7 us` / `1.76 KB` versus `GreaterThanRuntimeFallbackInvalidAsync` at about `2.2 us` / `2.62 KB`.
- The repo-local annotation-hygiene follow-on is now active too: the new `.agents\skills\annotation-hygiene\SKILL.md` captures the approved JetBrains-annotation policy, and the first runtime-model, planning, and validation seam passes are now in the tree.
- `RequiredIf` and `RequiredIfNot` are now active too: both consumer-facing attributes are back in `Blueprint.Validation`, the per-property planner recognizes them as required-like rules so downstream validators short-circuit correctly, and `ValidationModule` emits direct conditional required checks for supported default-constructor scalar/null comparisons while falling back to `ValidationRuntime.ValidateProperty(...)` for arrays, custom messages, display-name shaping, and unsupported comparison shapes.
- `BPV003` and `BPV004` now reject missing referenced-property names for `RequiredIfAttribute` and `RequiredIfNotAttribute`, extending the build-time property-reference validation family beyond `CompareAttribute` and `GreaterThanPropertyAttribute`.
- The current `ShortRun` benchmark evidence now also shows `RequiredIfInvalidAsync` at about `2.8 us` / `2.7 KB` versus `RequiredIfRuntimeFallbackInvalidAsync` at about `4.8 us` / `3.23 KB`; `RequiredIfNotInvalidAsync` is about `4.3 us` / `2.98 KB` versus `RequiredIfNotRuntimeFallbackInvalidAsync` at about `5.3 us` / `3.49 KB`, so the current short run now shows both the expected allocation drop and a mean-time win for the retained conditional-required paths.
- `NotAllowedIf` is now active too: the consumer-facing attribute is back in `Blueprint.Validation`, `ValidationModule` emits a direct conditional forbidden-value check for supported default-constructor scalar comparisons, and unsupported shapes continue to flow through `ValidationRuntime.ValidateProperty(...)` for display-name shaping, array dependent values, null dependent values, and other fallback-only contracts.
- `BPV005` now rejects missing referenced-property names for `NotAllowedIfAttribute`, completing the retained validation property-reference diagnostic family.
- The current `ShortRun` benchmark evidence now also shows `NotAllowedIfValidAsync` at about `3.4 us` / `2.14 KB`, `NotAllowedIfInvalidAsync` at about `3.8 us` / `3.04 KB`, and `NotAllowedIfRuntimeFallbackInvalidAsync` at about `5.7 us` / `3.41 KB`; this slice continues to deliver the expected invalid-path allocation drop, and this rerun now also shows a mean-time win over the display-name fallback path.
- The retained-validator queue is now clear; the next follow-on after this validation parity pass is the deferred no-exception validation-failure redesign.
- The deferred no-exception validation-failure redesign is now complete locally too: generated property-validation and whole-object fallback branches no longer throw `Blueprint.Validation.ValidationException`, and instead short-circuit directly to canonical `ValidationFailedResult` while still tagging the activity with `otel.status_code=OK`; `ValidationFailedOperationResult` remains as the migration alias, and the generated pipeline also keeps compatibility translation catches for both legacy `Blueprint.Validation.ValidationException` callers and framework `System.ComponentModel.DataAnnotations.ValidationException` runtime-fallback seams.
- The current `ShortRun` benchmark evidence now reflects the exception-free generated invalid path: `RequiredOnlyInvalidAsync` is about `3.2 us` / `2.27 KB` versus `RequiredFallbackInvalidAsync` at about `6.7 us` / `3.36 KB`, `RequiredRuntimeFallbackInvalidAsync` is about `3.8 us` / `2.48 KB`, `RequiredIfInvalidAsync` is about `2.8 us` / `2.7 KB` versus `RequiredIfRuntimeFallbackInvalidAsync` at about `4.8 us` / `3.23 KB`, `NotAllowedIfInvalidAsync` is about `3.8 us` / `3.04 KB` versus `NotAllowedIfRuntimeFallbackInvalidAsync` at about `5.7 us` / `3.41 KB`, and whole-object `ValidatableObjectFallbackInvalidAsync` is about `3.0 us` / `2.28 KB`.
