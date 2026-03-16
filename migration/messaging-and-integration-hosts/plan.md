# Messaging and integration hosts

- Status: Planned
- Priority: P3
- Classification: Consumer-space extension seam / alternate host
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`, `../background-tasks/plan.md`

## Why this module exists

The legacy ecosystem supports more than HTTP:

- alternate hosts
- message-driven execution models
- bus integrations such as Rebus
- consumer-specific pipeline extensions

Signature365 proves that these seams are real and valuable. The new rewrite must expose them intentionally rather than leaving consumers to fork the generator.

## Evidence

- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\IOperationExecutorBuilderScanner.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Api\OperationsHandlerScanner.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.BlueprintRebus\RebusHandleMessageOperationExecutorBuilderScanner.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\SetTenantMiddleware.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\ResourceExpandMiddleware.cs`

## Migration goal

Provide explicit, public seams for alternate hosts and handler-discovery strategies, while deciding what belongs in:

- core Blueprint
- official companion packages
- consumer-owned generator packages

## Runtime support to build

- alternate host registration model
- alternate handler-source registration model
- per-host descriptor metadata
- optional message/consumer runtime helpers
- package hooks for consumer-specific pipeline contributors

## Consumer configuration and activation

This plan should enable:

- package-provided handler scanners
- package-provided host exposure rules
- consumer-owned source-generator packages that compose `BlueprintSourceGenerationConfig`
- thin runtime builder helpers that activate the relevant modules/services

The generator composition root must be public and stable enough for consumers to build on.

## Worked example expectation

A consumer should be able to migrate a Signature365-style extension in one of two supported ways:

1. the preferred path: ship a thin generator package that composes official and custom modules
2. the fallback path: register a constrained runtime middleware contribution in a known stage when full generator participation is not justified

The preferred path should be able to express examples such as:

- Rebus message-consumer discovery
- a tenant/context enrichment step
- package-provided message transaction coordination

## Compile-time checks to add

- alternate host enabled without compatible operation metadata
- multiple handler sources producing illegal ambiguity
- message-handler covariance/contravariance rules that would change semantics unexpectedly
- consumer package uses module attributes without including the matching generator module

## Observability and debuggability

- manifest of active modules/packages
- handler-source provenance per operation
- host exposure summary
- diagnostics that explain package ordering and activation failures

## Explicit decisions

- Treat `ApiOperations` / `OperationsHandlerScanner` behavior as core and move it out of this extension bucket.
- Rebus integration should be treated as consumer-space or companion-package functionality, not core Blueprint.
- `SetTenantMiddleware` is evidence for required hooks, not evidence that tenant logic itself belongs in core.
- Consumer-owned generator packages are a supported scenario and must be designed for, not tolerated accidentally.
- A constrained runtime middleware fallback is acceptable as an escape hatch, but it is not the default extension model.

## Sequencing

Implement after the core host and pipeline model exists; package-specific integrations can then build on stable seams.
