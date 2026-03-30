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
| Multiple outcomes | Use `Results<T1, T2>` when an operation contract needs to declare one of two host-agnostic semantic result shapes |
| App root | Create a `partial` `BlueprintApp` with `[BlueprintAppScan]`; use `FromAssemblyOf = typeof(...)` when operations live in another assembly |
| Generator wiring | Reference Blueprint's generator as an analyzer so compile-time generation runs; in this repo the sample uses `ProjectReference` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"` |
| HTTP host | Map the generated surface with `MapBlueprintApi<TApp, TExec>()` |
| Validation | Prefer declarative `ValidationAttribute` usage; Blueprint generates validation from those contracts, and the current retained Blueprint-specific set now includes `Blueprint.Validation.GreaterThanPropertyAttribute` |
| Debugging | Prefer build diagnostics, generated output, manifests, and sample patterns over runtime guesswork |

## Validation contract

- Treat validation attributes as compile-time inputs first and runtime behavior second; if the build can prove the contract is wrong, expect a diagnostic instead of a runtime repair path.
- `Blueprint.Validation.GreaterThanPropertyAttribute` is now part of the retained Blueprint-specific validation surface for property-to-property greater-than checks.
- The retained Blueprint-specific conditional validator set now includes `GreaterThan`, `RequiredIf`, `RequiredIfNot`, and `NotAllowedIf` alongside `GreaterThanProperty`; verify the exact fast-path versus fallback shape before relying on a specific constructor or comparison form.

## Result contract

- The first public semantic-union surface is `Results<T1, T2>` in `Blueprint.Model`.
- Plain non-null handler returns currently normalize to typed `Blueprint.Runtime.Exec.OkResult<T>`, and `void`/`Task` handlers normalize to the current no-result runtime shape.
- Explicit `Blueprint.OperationResult` returns pass through unchanged; matching `OkResult<T>` handlers remain compatible with plain declared contracts like `IQuery<T>` when the payload type matches, and `Results<T1, T2>` handlers must resolve to one of the declared member result types.
- Declared semantic result contracts now also surface descriptor feature data through `OperationResultOutcomesFeatureData`, which flattens `Results<T1, T2>` into ordered `OperationResultOutcome` entries for runtime consumers that inspect `Configure()`.
- Invalid `Results<T1, T2>` contracts now also fail descriptor generation with `BP111` before `OperationResultOutcomesFeatureData` can drift away from the declared semantic member set; plain CLR payload members and nested unions are rejected rather than projected as descriptor outcomes.
- `MapBlueprintApi(...)` now starts the runtime migration away from self-executing results by translating `OkResult<T>`, `CreatedResult<T>`, `AcceptedResult<T>`, `NotFoundResult<T>`, `ConflictResult<T>`, host-neutral `NoResultOperationResult`, and the first host-owned no-content result `Blueprint.Runtime.Http.HttpNoContentResult` through `Blueprint.Runtime.Http.HttpOperationResultExecutor`; unsupported legacy results still fall back to direct `OperationResult.ExecuteAsync(...)` for now.
- Treat `NoResultOperationResult` as the default semantic success shape that currently maps to empty HTTP `200 OK`, and use `Blueprint.Runtime.Http.HttpNoContentResult` when the consumer contract intentionally needs an explicit HTTP `204 No Content` response instead of folding that transport choice back into the core result taxonomy.
- Use `Blueprint.Runtime.Exec.CreatedResult<T>` when the consumer contract should communicate "resource created" semantics with a response body; the current HTTP host maps it to JSON `201 Created`, so it stays distinct from both `OkResult<T>` (`200`) and the empty-body `KeyOnlyResourceEventResult` path.
- Use `Blueprint.Runtime.Exec.AcceptedResult<T>` when the consumer contract should communicate "accepted but not yet completed" semantics with a response body; the current HTTP host maps it to JSON `202 Accepted`, so it stays distinct from both immediate `OkResult<T>` success (`200`) and created-resource responses (`201`).
- Use `Blueprint.Runtime.Exec.NotFoundResult<T>` when the consumer contract should communicate a missing-resource outcome with a response body; the current HTTP host maps it to JSON `404 Not Found`, so it stays distinct from success-shaped responses and from state/conflict results.
- Use `Blueprint.Runtime.Exec.ConflictResult<T>` when the consumer contract should communicate a domain or state conflict with a response body; the current HTTP host maps it to JSON `409 Conflict`, so it stays distinct from success-shaped responses and can participate in declared `Results<T1, T2>` contracts without collapsing into the `200`/`201`/`202` paths.
- Use `Blueprint.Runtime.Exec.KeyOnlyResourceEventResult` for the first host-neutral key-only resource-event semantic shape when the result carries event/resource identity but intentionally returns no HTTP body; the current HTTP host translates it to empty `201 Created` responses by convention.
- Host-exposed operations now also fail build-time validation with `BP108` when the declared semantic result contract includes a shape that the current enabled HTTP executor path does not translate.
- Host-exposed `Results<T1, T2>` contracts now also fail build-time validation with `BP109` when multiple supported semantic result members would still collapse to the same current HTTP response shape, such as multiple `OkResult<T>` members that all translate to `200` JSON responses.
- Host-exposed `Results<T1, T2>` contracts now also fail build-time validation with `BP110` when supported members still share the same HTTP status code without explicit host/client discrimination metadata, such as `OkResult<T>` plus `NoResultOperationResult` both mapping to `200 OK`.
- Host-exposed `Results<T1, T2>` contracts now also fail build-time validation with `BP112` when they try to mix host-neutral `NoResultOperationResult` with host-owned no-content/status-only result families such as `Blueprint.Runtime.Http.HttpNoContentResult`; that semantic split is deliberate, so use one shape or the other instead of combining them.
- Nullable plain-value handler returns are a build-time error, and broader host translation remains follow-on work.

## Working loop

1. Start from the consumer contract: operation -> app root -> host wiring.
2. Check build diagnostics before editing runtime code.
3. Compare against the sample consumer story when available (`Blueprint.SourceGenerator.Sample` and `.Sample.Contract` in the Blueprint repo).
4. For migrations, work in dependency order: bootstrap and discovery, pipeline and result handling, auth and validation, resource and link behavior, OpenAPI and inspection, then consumer-specific integrations.

## Common mistakes

- Treating Blueprint as runtime-discovery-first
- Hand-registering operations instead of relying on generated registration
- Forgetting `partial`, `[BlueprintAppScan]`, or analyzer wiring
- Expecting runtime fallbacks to repair a missing compile-time contract
- Ignoring generator diagnostics or generated output diffs

This is the canonical consumer-installable Blueprint skill. If Blueprint changes the consumer contract, update this skill in the same change as the code and docs.
