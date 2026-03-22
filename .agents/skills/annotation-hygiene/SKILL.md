---
name: annotation-hygiene
description: Use when adding, reviewing, or cleaning up JetBrains.Annotations usage in blueprint-source-gen, especially `UsedImplicitly`, `MustDisposeResource`, `MustUseReturnValue`, implicit-use warnings, runtime-model contracts, generator planning seams, validation paths, or focused annotation consistency slices
---

# Annotation Hygiene

## Overview

`JetBrains.Annotations` is available repo-wide, but this repo should use it deliberately, not decoratively. Add annotations where they sharpen a contract that future contributors, Rider/ReSharper, or source-generator maintainers can easily misuse; skip them when nullable reference types or existing BCL flow attributes already say enough.

## When to Use

- Touching public or cross-module contracts that are discovered indirectly rather than called directly
- Seeing Rider/ReSharper implicit-use warnings or questions about `UsedImplicitly`, `MustDisposeResource`, `MustUseReturnValue`, or related JetBrains attributes
- Reviewing `Try*` seams in runtime, planning, validation, or generator code where nullable intent is easy to lose
- Cleaning up analyzer warnings, implicit-use warnings, or disposal/lifetime hints in shared helpers
- Taking a broader annotation consistency pass across a focused slice of the repo

Do not use this skill to justify repo-wide annotation churn with no contract improvement.

**REQUIRED BACKGROUND:** Use `using-dotnet` and `dotnet-advisor` for .NET work. In blueprint-source-gen, `dotnet-advisor` should route JetBrains annotation questions here instead of stretching a broader .NET skill. Use `dotnet-csharp` for nullable-flow background, and `extending-blueprint` when the touched code changes Blueprint internals.

## Core categories

| Category | Prefer here | Good fit in this repo | Avoid |
| --- | --- | --- | --- |
| Nullable flow and `Try*` contracts | First prefer existing BCL flow attributes such as `[NotNullWhen(true)]` | `BlueprintApp.TryFindOperation(...)`, `DescriptorPlan.TryGetMetadata(...)`, validation fast-path `TryCreateRule(...)` seams | Stacking JetBrains nullability attributes on top of clear NRT + BCL flow contracts |
| Implicit use / discovery contracts | `[UsedImplicitly]`, usually with `ImplicitUseTargetFlags.WithInheritors` | Marker-style model interfaces and extension points that are consumed via inheritance, scanning, or generation instead of direct instantiation | Annotating every public type “just in case” |
| Resource/lifetime hints | `[MustDisposeResource]` | Custom enumerator or owned-resource returns where callers could accidentally leak a disposable result | Adding disposal annotations to ordinary borrowed values |
| Optional contract hints | `JetBrains` attributes like `[Pure]` or `[MustUseReturnValue]` only when ignoring the result is a real bug | Small helper APIs whose return value encodes important planning or validation state | Blanket use on trivial getters, LINQ helpers, or fluent builders |

## High-value seams

### 1. Runtime and model contracts

- Keep discovery-heavy contracts explicit.
- Existing examples already show the bar:
  - `src\Blueprint.Runtime.Model\ICommand.cs`
  - `src\Blueprint.Runtime.Model\IQuery.cs`
  - `src\Blueprint.Runtime.Model\BlueprintApp.cs`
- When an interface exists mainly to be implemented, scanned, or generated against, `UsedImplicitly` can be appropriate.
- When a runtime lookup follows a `Try*` pattern, keep flow analysis crisp and non-redundant.

### 2. Planning and generator seams

- Planning APIs are consumed across modules and generator passes, so annotation noise multiplies quickly.
- Prioritize the seams where a failed lookup changes later planning logic:
  - `src\Blueprint.SourceGenerator\Planning\DescriptorPlan.cs`
  - `src\Blueprint.SourceGenerator\Planning\IOperationLookup.cs`
- If a helper returns nullable state, express the invariant once in the most precise place. Do not annotate every intermediate local or wrapper method.

### 3. Validation fast-path and fallback boundaries

- Validation planning is a hotspot for `Try*` contracts and “supported vs fallback” decisions.
- Review:
  - `src\Blueprint.SourceGenerator\Modules\Validation\IValidationFastPathStrategy.cs`
  - `src\Blueprint.SourceGenerator\Modules\Validation\ValidationPropertyPlanner.cs`
  - concrete strategy files under `src\Blueprint.SourceGenerator\Modules\Validation\`
- These seams benefit from deliberate contracts because one missed nullability hint can force suppressions or obscure whether the pipeline stays on the fast path.

### 4. Helper and collection infrastructure

- Shared helpers deserve annotations when they encode ownership or indirect usage that the type system alone does not make obvious.
- Existing example:
  - `src\Blueprint.SourceGenerator\Helpers\EquatableList.cs`
- Favor annotations that explain a real lifetime or discovery rule, not annotations that simply restate the return type.

## Working rules

1. Start from the contract risk: what could a future maintainer misunderstand here?
2. Prefer built-in nullable reference types and `System.Diagnostics.CodeAnalysis` flow attributes for `Try*` and null-state contracts already following that pattern.
3. Add JetBrains annotations when they convey something the BCL/NRT surface does not: implicit use, disposal expectations, or an important ignored-result contract.
4. Keep annotation style consistent within the touched slice. If one interface hierarchy uses `UsedImplicitly`, extend that pattern intentionally or leave it alone.
5. Remove conflicting or redundant annotations rather than piling on more.

## What not to do

- Do not annotate every parameter, property, or return value because the package is available.
- Do not mix nullable signals that disagree with each other (`string?` plus annotations implying always non-null, or JetBrains nullability layered over an already-clear BCL flow contract).
- Do not use annotations to paper over uncertain logic. Fix the contract or the control flow first.
- Do not broaden a focused change into repo-wide cleanup unless the todo explicitly calls for that slice.
- Do not add `UsedImplicitly` to silence warnings on types that are actually dead or should be referenced directly.

## Review checklist

### For a touched file

- Is there a real contract ambiguity, or would NRT/BCL flow analysis already be enough?
- If this is a `Try*` method, is the null-state expressed once and precisely?
- If the type is discovered indirectly, is `UsedImplicitly` justified by actual repo behavior?
- If a method returns something disposable or easy to ignore incorrectly, is there a concrete lifetime/result-usage reason to annotate it?
- Did the change reduce suppressions/noise instead of increasing them?

### For a broader cleanup slice

- Pick one seam at a time: runtime-model, planning, validation, or shared helpers.
- Establish the local convention from nearby code before editing multiple files.
- Prefer consistency fixes that remove redundancy or align equivalent contracts.
- Re-read every touched file for conflicting signals, especially around `Try*` methods and nullable returns.
- Stop when the slice has a clear rule; do not chase “perfect coverage.”

## Common mistakes

- Treating JetBrains annotations as the primary nullability system in a repo that already uses NRT and BCL flow attributes
- Adding annotation noise to generated-code-adjacent helpers without improving the contract
- Forgetting that discovery/generation interfaces may need `UsedImplicitly` while ordinary internal types do not
- Leaving a seam half-converted so neighboring files express the same contract in incompatible ways
