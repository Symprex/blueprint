# Validation

- Status: Planned
- Priority: P1
- Classification: Core middleware
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`

## Why this module exists

Validation is part of Blueprint's normal execution contract, not an optional afterthought. The legacy library supports both DataAnnotations and Blueprint-specific validation attributes, and returns `ValidationFailedOperationResult` rather than generic unhandled errors.

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
- A stable `ValidationFailedOperationResult` path
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

## Sequencing

Implement as part of the first parity wave, alongside execution and auth.
