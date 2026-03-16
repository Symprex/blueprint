# Auditing and observability

- Status: Planned
- Priority: P1
- Classification: Cross-cutting
- Depends on: `../foundation-module-system/plan.md`, `../pipeline-composition-and-execution/plan.md`

## Why this module exists

The legacy library has dedicated logging and auditing middleware, APM/activity frames, metadata providers, and attribute-based audit controls. The current rewrite already has a good tracing/logging baseline, but it does not yet have a full module plan for audit behavior or generated inspection surfaces.

## Evidence

- Current: `src\Blueprint.Runtime\Diagnostics\BlueprintActivitySource.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\LoggingMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\AuditMiddleware.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Diagnostics\ActivityFrame.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\BuiltinBlueprintMiddlewares.cs`

## Migration goal

Make traces, logs, audits, and generated debug surfaces part of the architecture from the start rather than bolted on after parity work.

## Runtime support to build

- per-operation activity/tracing plan driven by the shared executor shell
- structured logging plan using `LoggerMessage`-style generated delegates
- optional auditing stage with repository abstraction
- context metadata provider hooks
- generated manifests that explain applied stages/modules

## Consumer configuration and activation

Preserve or replace with thin equivalents:

- `.AddAuditing(...)`
- `[Sensitive]`
- `[DoNotAudit]`
- `[DoNotRecordPerformanceMetrics]`

Tracing and baseline logging should come from the shared executor shell rather than from an opt-in `.AddLogging()` activation switch. Auditing can remain an explicit configuration concern because it normally depends on repositories and policy.

Sensitivity markers need an explicit cross-cutting rule: they affect audit/logging behavior by default, but they do not silently remove fields from HTTP/OpenAPI contracts unless an explicit documentation policy opts into that behavior.

## Compile-time checks to add

- audit attributes used when auditing is not enabled
- missing audit repository when auditing is activated
- unsupported audit masking placement
- observability modules that conflict over the same stage slots or metadata keys

## Observability and debuggability

This module owns the AI-friendly inspection surface story:

- applied module/stage manifest per operation
- handler source summary
- host exposure summary
- trace/log event catalog
- optional generated diagnostics page or dev-only inspection endpoint later

## Explicit decisions

- Observability is not just runtime tracing; it includes generated manifests and explanation surfaces.
- Keep the current AOT-friendly logging approach and expand it.
- Make tracing/logging baseline behavior part of the shared executor shell.
- Make auditing optional but design the hooks now so other modules can rely on them.

## Sequencing

Start early and keep evolving alongside other modules. This is a cross-cutting workstream, not a late-phase polish task.
