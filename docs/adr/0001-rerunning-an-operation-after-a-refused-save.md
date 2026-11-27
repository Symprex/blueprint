# ADR 0001: Rerunning an operation after a refused save

- **Status:** Accepted (2026-11-27)
- **Date:** 2026-11-27
- **Issues:** Symprex/blueprint#200, part of #199. Builds on #197 / #198.

## Context

#198 turns a unit of work save that the store refuses because another request wrote first
(`ConcurrencyException`, `DocumentAlreadyExistsException`) into `SaveRefusedException`, answered with the
operation's `[SaveRefusedError]` 409. The save runs after the handler has returned, so a find-or-create handler
(Signature365's *Ensure organisation for a HubSpot company*) never sees the clash and cannot return the winner.
Today such handlers save part-way, in a session of their own, to catch it.

Rerunning the operation fixes that with no special code: the rerun's lookup finds the winner. But a rerun is only
safe if everything the refused attempt did is either thrown away or repeatable. This ADR sets out, from the code as
it is, what each kind of side effect does on a rerun, and what Blueprint enforces rather than documents.

The generated pipeline, for an operation that saves (no `[DoNotSaveChanges]`), runs in this order inside one
`ExecuteCoreAsync` call (`UnitOfWorkModule`, `RebusTransactionModule`, `MessageOutboxModule`, `AuditingModule`,
`BackgroundTaskFlushModule`):

1. tenant, population, user context, authorisation, validation;
2. `BeginUnitOfWorkAsync`;
3. `using var rebusTransactionScope = new RebusTransactionScope()` (when Rebus is registered, the operation has no
   `[NoRebusTransaction]` and the context is not nested), enlisted when a unit of work is registered;
4. the handler;
5. a resource event's self query, which saves first (`ResolveResourceEventSelfQueryAsync`);
6. `SaveUnitOfWorkAsync` — **the refusal is thrown here, or at step 5**;
7. `CommitMessageOutboxAsync`, the audit sink, `FlushBackgroundTasksAsync`;
8. `rebusTransactionScope.CompleteAsync()`.

Steps 7 and 8 are success-guarded: a refused save throws past them.

## Decision

### The rerun

An operation opts in with **`[RetryOnConflict(retries)]`** (default 1, at most 5). When an attempt throws the refused
save's problem (an `ApiException` whose inner exception is `SaveRefusedException`, from step 5 or 6), Blueprint:

1. creates a **fresh DI scope** from the operation's `IServiceScopeFactory`, and points the context's
   `ServiceProvider` at it, disposing the previous rerun's scope if there was one;
2. runs the **whole** generated pipeline body again in it — tenant and user context included, since they are read
   from the scope — on the same `ApiOperationContext` and operation instance;
3. after the last rerun is refused too, lets the refusal through, so the answer is #198's 409.

The first attempt runs in the host's scope (the HTTP request's, the Rebus message's), which Blueprint does not own
and cannot dispose; what it holds is never committed and is disposed with the host's scope. The last rerun's scope
stays the context's until the host is done with the result: registered with `HttpResponse.RegisterForDisposeAsync`
when there is an `HttpContext`, otherwise disposed when the pipeline returns and the host's provider restored.

Only a **root** execution is rerun. A nested execution (`context.IsNested`) lets the refusal through to its root,
whose scope and unit of work it shares: rerunning just the nested part would rerun it against a session the root
has already written to. A cancelled operation is not rerun.

### What each side effect does on a rerun

| Side effect | What happens to the refused attempt's | Why (code) | Blueprint |
| --- | --- | --- | --- |
| **Store writes** in the unit of work | Discarded | `MartenUnitOfWork.SaveChangesAsync` is one transaction; the refusal rolls it back. The rerun gets a fresh session from the fresh scope, with none of the refused attempt's tracked documents. | Enforced: fresh scope. |
| **Writes a handler saved part-way** (its own `SaveChangesAsync`, or a nested command's save on the shared session) | **Kept** — committed before the refusal | A nested command saves its root's session (`UnitOfWorkModule.IsSkippedWhenNested` is false for commands). | Documented. The rerun must tolerate them, as a find-or-create does by finding them. |
| **Outbox: Rebus sends and publishes** (`IBus`, inside the pipeline's `RebusTransactionScope`) | Discarded | The scope is `using`-declared inside the attempt and is completed only at step 8; a refusal disposes it uncompleted, and Rebus drops its outgoing messages. The ambient context is set inside the async attempt, so it does not leak to the rerun. | Enforced: the generator refuses `[RetryOnConflict]` with `[NoRebusTransaction]`, where sends join the host's transaction and survive the refusal. |
| **Outbox rows a drain writes into the session** (`AddBlueprintOutboxListener`) | Discarded | Written during the refused save, in its transaction. | Enforced by the store transaction. |
| **`IMessageOutbox`** (`MessageOutboxModule`) | Discarded | `CommitAsync` is success-guarded after the save; the contract says an uncommitted outbox discards on dispose. It is scoped, so the rerun gets an empty one. | Enforced: fresh scope. Documented: an `IMessageOutbox` must be scoped (as `IMessageOutbox`'s docs already say). |
| **Domain events** (`DomainEventDispatchingSessionListener`, `OutboxPublishingSessionListener`) | In-process handlers **ran**; their store writes and bus publishes are discarded as above | They run in `BeforeSaveChangesAsync`, inside the refused save. | Documented: an in-process domain event handler is part of the operation for this rule; its outside calls repeat (below). |
| **Background tasks** (`BackgroundTaskFlushModule`) | Discarded | `BackgroundTaskScheduler` buffers per scope and dispatches in `RunNowAsync`, success-guarded after the save. Blueprint registers it scoped. | Enforced: fresh scope. Documented: a custom scheduler must be scoped. |
| **Audit** (`AuditingModule`, resource event audit) | Not written | The audit sink runs success-guarded after the save. A refusal is not audited; the attempt that succeeds is audited once. | Enforced by the pipeline order. |
| **Nested operations** the handler runs | Discarded with the attempt, except their saves (above) | They share the attempt's scope, outbox, scheduler and Rebus scope. | Enforced: fresh scope. |
| **Direct outside calls** (HTTP, Chargebee, email, anything not through the above) | **Repeated** | Nothing Blueprint holds can take them back. | Documented, with the rule below. Not detectable at compile time in general. |

### The rule for outside calls

`[RetryOnConflict]` is the author's statement that the handler is safe to run again. A handler that calls outside
directly is safe only if every such call is **idempotent across reruns**:

- it carries an idempotency key that is **the same on every rerun** of the operation, and
- the rerun sends **the same request**.

Chargebee is the case in hand: its `chargebee-idempotency-key` lasts 30 minutes, answers a repeat whose body differs
with a 422, replays a cached 5xx, and does not cover the estimate APIs. So a rerun that recomputes a body (a new
timestamp, a re-read price) gets a 422, and a key that changes per rerun gets a second charge. A key made from
something the rerun does not change — the operation's own identity (a Rebus message id, as Signature365's
`ChargebeeIdempotencyKeys.ForMessage` already does) plus the call's purpose — passes both.

Better still, and the direction this ADR sets: **move the outside call out of the handler** into a message the
handler sends through the outbox, handled by its own handler keyed by message id. Then the refused attempt's message is
discarded with it (table above) and the call happens once, after the save that made it right. That is what makes a
handler rerunnable without a rule to follow.

Blueprint does not yet give an HTTP operation a key that stays the same across reruns and is not from the caller.
That is a separate piece (see *Later*).

### What Blueprint enforces

Compile time (`BPP` diagnostics in `PersistenceDiagnosticDescriptors`):

- `[RetryOnConflict]` on an operation that does not save (`[DoNotSaveChanges]`) — there is no save to refuse.
- `[RetryOnConflict]` with `[NoRebusTransaction]` — sends would survive the refusal.
- `retries` outside 1–5.

Run time:

- every rerun in a fresh scope, so scoped buffers (unit of work, `IMessageOutbox`, background task scheduler, Rebus
  scope) cannot carry a refused attempt's effects into a later one;
- root executions only; never after cancellation;
- each rerun logged and recorded on the operation's activity (`blueprint.operation.save_refused_retry`).

Everything else in the table is documented, in `docs/integration-packages.md` and the `using-blueprint` skill.

### Shape 2, a conflict handler (proposed, not built)

`IHandleRefusedSave<TOperation, TResult>`, called after the last refused attempt in a fresh scope and unit of work,
returning a replacement result or `null` for the 409. It uses the same plumbing: the generated pipeline body becomes
one attempt, and the runtime helper that reruns it takes the conflict handler as what to do once reruns are spent.
The proposal is on #200. Building it is a separate decision.

## Consequences

- A find-or-create handler drops its part-way save and its own catch, and gains `[RetryOnConflict]`.
- The generated pipeline of an opted-in operation splits into an attempt method and a wrapper that reruns it. Other
  operations' output is unchanged.
- A rerun costs a DI scope and a second run of the pipeline's front half (authorisation, user context), only on a
  refusal.
- Scoped services a host seeds into its own scope before running the pipeline are not in a rerun's scope. Nothing in
  Blueprint does that today (the HTTP host uses the request scope as it is; the Rebus host keeps message state on the
  context and in `IncomingMessageContext`, an async local); a consumer that does must carry it on the context instead.
- In a Rebus handler, Rebus's own redelivery still follows if the last rerun is refused.

## Alternatives considered

- **Rerun only the handler, in the same scope.** Cheaper, but the refused attempt's background tasks and
  `IMessageOutbox` messages are still buffered in the scope and would be committed by the rerun's success, and the
  Marten session still tracks the refused documents. Rejected: it makes the outbox unsafe, which is the one guarantee
  the retry must keep.
- **Rerun from the host** (the HTTP endpoint, the Rebus activator, re-dispatching with a new scope). Each host knows
  how to build its scope, but each would need its own retry, and a nested or in-process `ExecuteAsync` caller would
  get none. Rejected for now; the scope seeding consequence above is the cost of not doing this.
- **Rely on the client or Rebus to retry.** Rebus already redelivers a failed message, but an HTTP caller gets a 409
  it must understand, and every client of every find-or-create must. Rejected: the server knows the rerun is safe.
- **Refuse `[RetryOnConflict]` on any handler that injects an HTTP client.** Catches the obvious case and misses the
  rest (a typed client, a domain service wrapping one, a domain event handler). Not done; may be revisited as a
  warning.

## Later

- **An attempt-stable operation key.** A key Blueprint gives every root execution, kept across reruns: the
  `Idempotency-Key` header when the caller sent one, the Rebus message id for a message, otherwise one made once per
  execution. Handlers derive outside-call keys from it.
- **Server-side idempotency keys** (#199, *Later*): a key → result record saved in the same unit of work. A concurrent
  duplicate clashes on that record, which is a refused save that this rerun then answers from the stored result.
- **Declared, side-effect-free handlers** (#199, *Later*): a handler returns its effects (a save, a message) instead of
  making them. A handler with no writes can always be rerun, so the rule above needs no author's statement; outside
  calls become messages by construction; and a save effect's `OnConflict` is shape 2 narrowed to one save, built on the
  same "once refused, in a fresh unit of work" hook.
