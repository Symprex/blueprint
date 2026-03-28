# Background tasks

- Status: Planned
- Priority: P3
- Classification: Optional host/module
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`

## Why this module exists

The legacy Blueprint ecosystem treats background tasks as first-class operations with their own host, scheduler abstractions, and optional Hangfire integration. They also participate in pipeline behavior through post-execution task flushing.

The new system needs a clear plan for both task-producing operations and task-executing hosts.

## Evidence

- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Tasks\BackgroundTaskRunnerMiddleware.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Tasks\`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Tasks.Hangfire\`
- Generated evidence: background task execution appears in legacy generated pipelines after successful handler execution

## Migration goal

Support background tasks as source-generated operations/hosts rather than as opaque runtime integrations.

## Runtime support to build

- background-task scheduler abstraction
- generated task host support
- post-success task flush stage
- recurring task metadata
- optional package integrations such as Hangfire

## Consumer configuration and activation

Preserve familiar shapes where sensible:

- `IBackgroundTask`
- task scheduler abstractions
- recurring task configuration
- optional builder/package helpers for Hangfire or other schedulers

## Compile-time checks to add

- task operations exposed without a task host
- missing serializer context coverage for queued payloads
- invalid recurring task metadata
- task operations that require unsupported return semantics
- package-specific attributes/options used when the package is not enabled

## Observability and debuggability

- task scheduling manifest
- correlation between the parent operation and queued tasks
- generated metadata for recurring schedules
- trace/log hooks for queueing and immediate flush behavior

## Explicit decisions

- Treat background tasks as a distinct host concept, not just "HTTP plus side effects".
- Keep core abstractions generic; keep concrete scheduler integrations in optional packages.
- Make serialization and scheduling metadata compile-time visible wherever possible.

## Sequencing

Implement after the base execution and host model exists, but before deeper messaging integrations.

A narrow prerequisite may land earlier in the operation-discovery workstream: a compile-time-only `IBackgroundTask` marker plus tasks host contributor can be introduced to provide the second concrete host contributor needed for host-exposure diagnostics. Keep that early prerequisite limited to host-specific marker and metadata flow; the scheduler/runtime/task-flush semantics in this plan still wait for the broader background-task workstream.

That early prerequisite has now landed: `Blueprint.Tasks.IBackgroundTask` and `TasksHostModule` exist to make task-host intent visible to compile-time discovery and runtime host indexing, but the scheduler abstractions, generated task host, recurring-task metadata, and post-success flush behavior in this plan remain deferred work.
