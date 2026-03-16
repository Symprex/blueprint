# Persistence and transaction coordination

- Status: Planned
- Priority: P3
- Classification: Consumer-space integration seam
- Depends on: `../foundation-module-system/plan.md`, `../pipeline-composition-and-execution/plan.md`

## Why this module exists

Signature365 uses Blueprint middleware for unit-of-work and outbox patterns such as:

- save changes after a successful command
- wrap execution in a transaction scope
- connect persistence to an outbox/message-bus transaction

These are important migration seams, but they should not force vendor-specific behavior into the core generator.

## Evidence

- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Data\Marten\SaveMartenChangesMiddleware.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Data\Marten\UseMartenAsOutboxMiddleware.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.BlueprintRebus\StartRebusTransactionScopeMiddleware.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.BlueprintRebus\CommitRebusTransactionScopeMiddleware.cs`

## Migration goal

Create generic pipeline seams for transaction and persistence coordination, then implement Marten and other persistence integrations in consumer space or dedicated companion packages instead of the core Blueprint package set.

## Runtime support to build

- around-execution stage hooks
- post-success and cleanup hooks
- access to shared transaction/persistence variables in the pipeline plan

## Consumer configuration and activation

Preserve the ability to express:

- save-changes behavior
- outbox registration behavior
- opt-out markers such as `[DoNotSaveChanges]` or equivalent
- package-specific registration helpers for Marten/EF Core/etc.

## Compile-time checks to add

- persistence/transaction attributes used without the required package/module
- missing required service types for an activated package
- conflicting ordering between transaction start/commit/save/outbox steps

## Observability and debuggability

- transaction lifecycle stages visible in the generated plan
- clear logs/tags for save-changes/outbox participation
- diagnostics when a package cannot supply the variables or services it requires

## Explicit decisions

- Keep the core contract generic; do not make Marten the core abstraction.
- Treat Marten integration as consumer-space rather than as planned core Blueprint functionality.
- Plan for consumer or companion package implementations rather than hard-coding one ORM strategy into the base generator.
- Treat transaction coordination as a stage-planning concern, not as ad-hoc string injection.

## Sequencing

Implement after the base pipeline composition model is in place.
